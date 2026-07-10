class SatlError(Exception):
    """Base exception carrying the public CLI exit code."""

    exit_code = 1


class UsageError(SatlError):
    exit_code = 2


class PreflightError(SatlError):
    exit_code = 3


class CatalogError(SatlError):
    exit_code = 4


class IntegrityError(SatlError):
    exit_code = 5


class TransactionError(SatlError):
    exit_code = 6
