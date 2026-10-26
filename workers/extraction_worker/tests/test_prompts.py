"""Frozen request fingerprints predate the prompt/resource refactor.

These cover the complete serialized requests (including whitespace, schema,
routing and limits), which must retain the existing cache keys.
"""

import hashlib
import json

import pytest

from experiments.gemma_benchmark import PROMPTS
from extraction.providers import GoogleClient, NvidiaClient, OpenRouterClient
from extraction.contracts import ImageOCRInput, SourceFiles
from extraction.prompts import load_text
from extraction.steps.analysis import ClassifyReceiptItemsStep
from extraction.analysis.item_classifier import ReceiptItemClassifier, build_request
from extraction.steps.parsing import GeminiParseStep
from extraction.steps.parsing.gemini import build_request as build_gemini_request
from extraction.analysis.receipt_images import ReceiptImageAnalyzer, build_vision_request
from extraction.workflows import vision_analysis_pipeline, vision_ocr_analysis_pipeline
from test_gemma import answer, inputs
from test_gemma_vision import response as vision_response, source
from test_gemini import response as gemini_response
from test_parsing import at, document, ocr


PROVIDERS = {"openrouter_free": OpenRouterClient, "openrouter_paid": OpenRouterClient,
             "google": GoogleClient, "nvidia": NvidiaClient}
REQUEST_HASHES = {
    "openrouter_free": "a95f46599e7b08583c9e4c1014c9067dc16034acf22e964a50a2f9fe3eca7224",
    "openrouter_paid": "002835281ea46a7fb1e88ceefe94b7896bd15d4b9539a3c689210c87625d4c68",
    "google": "1ce6340d9d40857563cf66e2200f44ab959566c81b2c267e4aeec408eae626ac",
    "nvidia": "084bd763dce8b8ea753ce7fd484b0407f82a589f4dd55730cd5a0f7a2fe2e149",
    "baseline": "002835281ea46a7fb1e88ceefe94b7896bd15d4b9539a3c689210c87625d4c68",
    "candidate": "55698d14b4133edb5425a2f25f8128b94cc80674842ca834bf3f29594df18133",
    "vision": "ad732351d93cdeba8ec1738d84010bd97bc38239028d38566dbe1f821fad175e",
    "vision_ocr": "20926be98bc73baf3b3d9b895d7d8568ed244dba3cfd92183516b573109f0d6a",
    "gemini": "b169b828535751958feb17641b1355f493dc4a76e1892f083d9bd32ba4e08510",
}


def context():
    return ocr(document(at("Mleko 1 x3,00 3,00A", 0, 50)))


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def envelope(raw):
    return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(raw)}}]}


@pytest.mark.parametrize("name", REQUEST_HASHES)
def test_default_serialized_requests_match_before_refactor(name):
    receipt, empty = inputs()
    if name in ("vision", "vision_ocr"):
        payload = build_vision_request([source()], context() if name == "vision_ocr" else None)
    elif name == "gemini":
        payload = build_gemini_request(context())
    elif name in PROMPTS:
        payload = ReceiptItemClassifier(OpenRouterClient("test-key"), allow_paid=True,
                                        prompt=PROMPTS[name]).build_request(receipt, empty)
    else:
        payload = ReceiptItemClassifier(PROVIDERS[name]("test-key"),
                                        allow_paid=name == "openrouter_paid").build_request(receipt, empty)
    assert digest(json.dumps(payload, ensure_ascii=False, sort_keys=True)) == REQUEST_HASHES[name]


@pytest.mark.parametrize("provider", PROVIDERS)
def test_classification_override_matches_metadata_and_separates_cache(provider, tmp_path, monkeypatch):
    sent = []
    def send(body):
        sent.append(json.loads(body))
        return envelope(answer())
    receipt, empty = inputs()
    for prompt in (" custom A\n", " custom A\n", " custom B\n"):
        client = ReceiptItemClassifier(PROVIDERS[provider]("test-key"), allow_paid=provider == "openrouter_paid",
                                       cache_dir=tmp_path, prompt=prompt)
        monkeypatch.setattr(client, "_generate", send)
        config = ClassifyReceiptItemsStep(client).config
        assert config["prompt_id"] == "custom"
        assert config["prompt_sha256"] == digest(prompt)
        assert client.run(receipt, empty)["items"]
    assert len(sent) == 2
    assert len(list(tmp_path.glob("*.json"))) == 2
    for payload, prompt in zip(sent, (" custom A\n", " custom B\n")):
        actual = (payload["systemInstruction"]["parts"][0]["text"]
                  if provider == "google" else payload["messages"][0]["content"])
        assert actual == prompt


@pytest.mark.parametrize("with_ocr", [False, True])
def test_vision_override_capture_and_cache_metadata(with_ocr, tmp_path):
    sent = []
    def policy(body, send):
        sent.append(json.loads(body))
        return envelope(vision_response())
    factory = vision_ocr_analysis_pipeline if with_ocr else vision_analysis_pipeline
    value = ImageOCRInput(SourceFiles((source(),)), context()) if with_ocr else SourceFiles((source(),))
    # The fourth run changes only OCR guidance. Image-only requests ignore it.
    for text, guidance in ((" A\n", " guidance A\n"), (" A\n", " guidance A\n"),
                           (" B\n", " guidance A\n"), (" B\n", " guidance B\n")):
        client = ReceiptImageAnalyzer(OpenRouterClient("test-key"), allow_paid=True, cache_dir=tmp_path, request_policy=policy,
                                  prompt=text, ocr_prompt=text, ocr_guidance=guidance)
        result = factory(client).run(value, capture_steps={"analyze"})
        assert result.ok
        assert [step.name for step in result.steps] == ["analyze", "validate"]
        assert "analyze" in result.captured_steps
        config = result.steps[0].config
        assert config["prompt_id"] == "custom" and config["prompt_sha256"] == digest(text)
        if with_ocr:
            assert config["ocr_guidance_id"] == "custom"
            assert config["ocr_guidance_sha256"] == digest(guidance)
    assert len(sent) == (3 if with_ocr else 2)
    assert sent[0]["messages"][0]["content"] == " A\n"
    if with_ocr:
        assert sent[-1]["messages"][1]["content"][-1]["text"].startswith(" guidance B\n")


def test_gemini_override_metadata_and_cache(tmp_path, monkeypatch):
    sent = []
    def send(body):
        sent.append(json.loads(body))
        return gemini_response()
    for prompt in (" custom A\n", " custom A\n", " custom B\n"):
        client = GeminiParseStep(GoogleClient("test-key"), cache_dir=tmp_path, prompt=prompt)
        monkeypatch.setattr(client, "_generate", send)
        assert client.config["prompt_id"] == "custom"
        assert client.config["prompt_sha256"] == digest(prompt)
        client.run(context())
    assert len(sent) == 2
    assert sent[0]["systemInstruction"]["parts"][0]["text"] == " custom A\n"
    assert len(list(tmp_path.glob("*.json"))) == 2


@pytest.mark.parametrize("bad", ["", " \n", 42])
def test_empty_or_invalid_overrides_fail_before_requests(bad):
    for provider, cls, options in (
        *[(transport, ReceiptItemClassifier, {}) for transport in PROVIDERS.values()],
        (OpenRouterClient, ReceiptImageAnalyzer, {"allow_paid": True}),
        (GoogleClient, GeminiParseStep, {}),
    ):
        with pytest.raises(ValueError, match="Prompt"):
            cls(provider("test-key"), prompt=bad, **options)
    for key in ("ocr_prompt", "ocr_guidance"):
        with pytest.raises(ValueError, match="Prompt"):
            ReceiptImageAnalyzer(OpenRouterClient("test-key"), allow_paid=True, **{key: bad})
        with pytest.raises(ValueError, match="Prompt"):
            build_vision_request([source()], **{key: bad})
    with pytest.raises(ValueError, match="Prompt"):
        build_request(*inputs(), prompt=bad)
    with pytest.raises(ValueError, match="Prompt"):
        build_gemini_request(context(), prompt=bad)


def test_resources_load_outside_working_directory_without_trimming(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    assert load_text("classify_items").startswith("Popraw literówki OCR")
    assert load_text("classify_items").endswith("\n")
    assert load_text("name_policy", package="experiments.prompts").startswith("\n")
    with pytest.raises(FileNotFoundError):
        load_text("missing")
