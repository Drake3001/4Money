"""Google generateContent transport shared by Gemma and Gemini tasks."""

import re

from .http import post_json, validate_api_key


class GoogleClient:
    provider = "google"
    provider_name = "Google AI Studio (direct)"

    def __init__(self, api_key):
        validate_api_key(api_key, "GEMINI_API_KEY")
        self._api_key = api_key

    def send(self, body, *, model, timeout=60):
        if not isinstance(model, str) or not re.fullmatch(r"[A-Za-z0-9._-]+", model):
            raise ValueError("Invalid Google model name")
        return post_json(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                         body, {"x-goog-api-key": self._api_key},
                         provider=self.provider_name, timeout=timeout)
