"""Frozen requests for taxonomy v2 and standalone discount instructions.

Fingerprints include prompts, schema, routing and limits; prompt edits deliberately
change cache keys while custom prompt metadata retains its existing contract.
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
    "openrouter_free": "9f6b18526c56b9072d459d6b09d11a11836c0874780acf7abab91fa3481b7ffc",
    "openrouter_paid": "05ee973f7fcc2e2c380f1a3e324d78bea059dea67bef11ee26b55a2ed3208e3b",
    "google": "375949ac27dce7135382bb5e8a50c5635e2c75e8c6123e65426faa8a6131b4ae",
    "nvidia": "6013669aead6a030b0a3ee7684ede1d76e87fe874862af6811f1168a356b1ae7",
    "baseline": "05ee973f7fcc2e2c380f1a3e324d78bea059dea67bef11ee26b55a2ed3208e3b",
    "candidate": "7e59c141c72bcbce1b8538f4d5db8d819e403177e43dc6169cc881658c6d637b",
    "vision": "9300ed2a707a99a410b941374134e5505c99cf4358d685237a14f77bac35a1f4",
    "vision_ocr": "b2fe63e0a5059229f34ad6c2b45110d31eab2ce17ad5cc36d3af2c6c38c1be7f",
    "gemini": "94243e7d966777e84a34b1db2687a58db9278aabb9e8bdc705f0d0035b21b461",
}


def context():
    return ocr(document(at("Mleko 1 x3,00 3,00A", 0, 50)))


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def envelope(raw):
    return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(raw)}}]}


@pytest.mark.parametrize("name", REQUEST_HASHES)
def test_default_serialized_requests_match_taxonomy_v2(name):
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
