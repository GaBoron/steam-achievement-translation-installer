using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Satl_Gui.Services;

public static class NetworkErrorMessage
{
    public static bool IsNetworkError(Exception exception) =>
        Find<HttpRequestException>(exception) is not null
        || Find<SocketException>(exception) is not null
        || Find<TimeoutException>(exception) is not null
        || Find<AuthenticationException>(exception) is not null
        || exception is OperationCanceledException;

    public static string Describe(Exception exception, string operation)
    {
        if (exception is OperationCanceledException)
        {
            return $"{operation}已取消。";
        }

        var request = Find<HttpRequestException>(exception);
        if (request?.StatusCode is { } status)
        {
            return status switch
            {
                HttpStatusCode.ProxyAuthenticationRequired =>
                    $"{operation}失败：代理服务器需要身份验证。请检查代理用户名和密码。",
                HttpStatusCode.Unauthorized =>
                    $"{operation}失败：服务器拒绝了身份验证信息。",
                HttpStatusCode.Forbidden =>
                    $"{operation}失败：服务器暂时拒绝访问，请稍后再试。",
                HttpStatusCode.NotFound =>
                    $"{operation}失败：服务器上没有找到需要的内容，可能是软件版本或下载源已变更。",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                    $"{operation}失败：服务器响应超时，请检查网络、DNS 或代理设置。",
                HttpStatusCode.TooManyRequests =>
                    $"{operation}失败：请求过于频繁，请稍后再试。",
                >= HttpStatusCode.InternalServerError =>
                    $"{operation}失败：在线服务暂时不可用，请稍后再试。",
                _ =>
                    $"{operation}失败：服务器返回了 HTTP {(int)status}，请稍后再试。",
            };
        }

        var socket = Find<SocketException>(exception);
        if (socket is not null)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData =>
                    $"{operation}失败：无法解析服务器地址。请检查 DNS 设置，或改回“跟随系统”。",
                SocketError.ConnectionRefused =>
                    $"{operation}失败：连接被拒绝。若正在使用代理，请确认代理已启动且地址和端口正确。",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    $"{operation}失败：当前网络无法到达服务器，请检查网络连接、DNS 和代理设置。",
                SocketError.TimedOut =>
                    $"{operation}失败：建立连接超时，请检查网络、DNS 或代理设置。",
                SocketError.ConnectionReset or SocketError.ConnectionAborted =>
                    $"{operation}失败：连接被中途断开，请稍后重试。",
                _ =>
                    $"{operation}失败：无法建立网络连接。请检查网络、DNS 和代理设置。",
            };
        }

        if (Find<AuthenticationException>(exception) is not null)
        {
            return $"{operation}失败：无法建立安全连接。请检查系统时间、证书或代理的 HTTPS 设置。";
        }
        if (Find<TimeoutException>(exception) is not null
            || exception is TaskCanceledException)
        {
            return $"{operation}失败：等待服务器响应超时，请检查网络、DNS 或代理设置。";
        }
        return $"{operation}失败：暂时无法连接到在线服务，请检查网络、DNS 或代理设置后重试。";
    }

    private static T? Find<T>(Exception exception) where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }
        return null;
    }
}
