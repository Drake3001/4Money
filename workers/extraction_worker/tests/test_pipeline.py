import pytest
from extraction.contracts import StepResult
from extraction.pipeline import Pipeline


class Increment:
    input_type = int
    output_type = int
    version = "1"
    config = {"amount": 1}

    def run(self, input):
        return StepResult(input + 1)


@pytest.mark.parametrize(
    "steps",
    [
        [],
        [("a", Increment()), ("a", Increment())],
        [("../a", Increment())],
        [("CON", Increment())],
    ],
)
def test_invalid_configuration(steps):
    with pytest.raises(ValueError):
        Pipeline(steps)


def test_connection_and_actual_types():
    class Wrong(Increment):
        input_type = str

    with pytest.raises(TypeError):
        Pipeline([("a", Increment()), ("b", Wrong())])
    result = Pipeline([("a", Increment())]).run("wrong")
    assert result.error.exception_type == "TypeError" and result.output is None


def test_failure_keeps_last_output_and_capture_is_optional():
    class Broken(Increment):
        def run(self, input):
            raise RuntimeError("oops")

    p = Pipeline(
        [("first", Increment()), ("failure", Broken()), ("skipped", Increment())]
    )
    result = p.run(1, True)
    assert result.output == 2 and result.captured_steps == {"first": 2}
    assert len(result.steps) == 2 and not result.steps[-1].completed
    assert result.error.step == "failure" and result.error.message == "oops"
    assert p.run(1).captured_steps == {}


def test_invalid_output_keeps_last_output():
    class Wrong(Increment):
        def run(self, input):
            return StepResult("wrong")

    result = Pipeline([("first", Increment()), ("bad", Wrong())]).run(0)
    assert result.output == 1 and result.error.exception_type == "TypeError"


def test_config_is_snapshot_and_removing_step_changes_flow():
    step = Increment()
    result = Pipeline([("one", step)]).run(0)
    assert result.output == 1
    assert Pipeline([("one", step), ("two", step)]).run(0).output == 2
    assert result.steps[0].config is not step.config


@pytest.mark.parametrize("selection", [{"first"}, ("first",), ["first"]])
def test_selective_capture_retains_only_named_successful_steps(selection):
    pipeline = Pipeline([("first", Increment()), ("second", Increment())])
    result = pipeline.run(0, capture_steps=selection)
    assert result.output == 2 and result.captured_steps == {"first": 1}
    assert pipeline.run(0, capture_steps=set()).captured_steps == {}
