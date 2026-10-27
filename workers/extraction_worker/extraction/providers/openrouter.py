"""OpenRouter chat-completions transport for text and image requests."""

from .http import post_json, validate_api_key

BASE_URL = "https://openrouter.ai/api/v1"


class OpenRouterClient:
    provider = "openrouter"
    provider_name = "OpenRouter"

    def __init__(self, api_key, *, base_url=BASE_URL):
        if base_url.rstrip("/") != BASE_URL:
            raise ValueError("Only the official OpenRouter URL is permitted")
        validate_api_key(api_key, "OPENROUTER_API_KEY")
        self._api_key = api_key

    def send(self, body, *, timeout=60):
        return post_json(BASE_URL + "/chat/completions", body,
                         {"Authorization": "Bearer " + self._api_key},
                         provider=self.provider_name, timeout=timeout)
