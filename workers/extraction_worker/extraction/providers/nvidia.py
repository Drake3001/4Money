"""NVIDIA's OpenAI-compatible chat-completions transport."""

from .http import post_json, validate_api_key

BASE_URL = "https://integrate.api.nvidia.com/v1"


class NvidiaClient:
    provider = "nvidia"
    provider_name = "NVIDIA NIM"

    def __init__(self, api_key):
        validate_api_key(api_key, "NVIDIA_API_KEY")
        self._api_key = api_key

    def send(self, body, *, timeout=60):
        return post_json(BASE_URL + "/chat/completions", body,
                         {"Authorization": "Bearer " + self._api_key},
                         provider=self.provider_name, timeout=timeout)
