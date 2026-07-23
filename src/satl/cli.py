from __future__ import annotations

import argparse
import sys
from typing import Sequence

from satl.cli_arguments import build_parser, default_data_dir
from satl.cli_protocol import emit_jsonl
from satl.errors import SatlError


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args: argparse.Namespace | None = None
    try:
        args = parser.parse_args(argv)
        return int(args.handler(args))
    except SatlError as exc:
        if args is not None and getattr(args, "jsonl", False):
            operation = {
                "cache": "cache-refresh",
                "petition": "petition-export",
            }.get(getattr(args, "command", None), str(args.command))
            emit_jsonl(operation, "error", {"message": str(exc), "exit_code": exc.exit_code})
        else:
            print(f"错误：{exc}", file=sys.stderr)
        return exc.exit_code
    except KeyboardInterrupt:
        print("已取消。", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
