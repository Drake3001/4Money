"""Shared JSON transport with explicit timeouts and no redirects or retries."""

import json
import re
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener


class ProviderHTTPError(RuntimeError):
    """Expose the status, never a response body or credentials."""

    def __init__(self, provider, status, hint):
        self.status = status
        self.code = "provider_unavailable" if status in (429, 503) else "provider_error"
        super().__init__(f"{provider} HTTP {status}; {hint}; transport made no automatic retry")


class ProviderTimeoutError(RuntimeError):
    code = "provider_timeout"


class ProviderNetworkError(RuntimeError):
    code = "provider_network_error"


class ProviderResponseError(RuntimeError, ValueError):
    """A provider response failed decoding; also preserves ValueError compatibility."""

    code = "provider_response_invalid"

    def __init__(self):
        super().__init__("Provider response is invalid, blocked, truncated or incomplete")


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def validate_api_key(api_key, variable):
    if not isinstance(api_key, str) or not re.fullmatch(r"[A-Za-z0-9_-]+", api_key):
        raise ValueError(f"Missing or invalid {variable}")


def post_json(url, body, headers, *, provider, timeout):
    request = Request(url, data=body, method="POST", headers={"Content-Type": "application/json", **headers})
    try:
        with build_opener(NoRedirect()).open(request, timeout=timeout) as response:
            return json.load(response)
    except HTTPError as exc:
        hint = ("provider server error" if exc.code >= 500 else
                "rate limit or project quota" if exc.code == 429 else
                "check key, model access and quota")
        raise ProviderHTTPError(provider, exc.code, hint) from None
    except TimeoutError:
        raise ProviderTimeoutError("Provider response timed out; no automatic retry was made") from None
    except URLError as exc:
        if isinstance(exc.reason, TimeoutError):
            raise ProviderTimeoutError("Provider response timed out; no automatic retry was made") from None
        raise ProviderNetworkError("Provider network request failed; no automatic retry was made") from None
    except OSError:
        raise ProviderNetworkError("Provider network request failed; no automatic retry was made") from None
    except (ValueError, UnicodeError):
        raise ProviderResponseError() from None
