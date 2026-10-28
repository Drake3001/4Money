"""A sequential pipeline; branching belongs to the caller."""

import re
from collections.abc import Collection
from copy import deepcopy
from time import perf_counter

from .contracts import PipelineError, PipelineResult, StepExecution, StepResult

_RESERVED_NAMES = {"CON", "PRN", "AUX", "NUL"}
_RESERVED_NAMES.update(
    f"{prefix}{i}" for prefix in ("COM", "LPT") for i in range(1, 10)
)


class Pipeline:
    def __init__(self, steps):
        self.steps = tuple(steps)
        if not self.steps:
            raise ValueError("Pipeline requires at least one step")
        names = set()
        previous = None
        for name, step in self.steps:
            if (
                not isinstance(name, str)
                or not re.fullmatch(r"[A-Za-z0-9_-]+", name)
                or name in names
                or name.upper() in _RESERVED_NAMES
            ):
                raise ValueError(f"Invalid or duplicate step name: {name!r}")
            names.add(name)
            if not isinstance(step.input_type, type) or not isinstance(
                step.output_type, type
            ):
                raise TypeError("Step types must be concrete Python classes")
            if previous is not None and not issubclass(previous, step.input_type):
                raise TypeError(f"Incompatible input for step {name}")
            if not isinstance(step.version, str) or not isinstance(step.config, dict):
                raise TypeError("Step version must be a string and config a dict")
            previous = step.output_type

    def run(self, input, capture_steps: bool | Collection[str] = False):
        result = PipelineResult()
        captured_names = frozenset() if isinstance(capture_steps, bool) else frozenset(capture_steps)
        value = input
        for name, step in self.steps:
            started = perf_counter()
            config = deepcopy(step.config)
            completed = False
            try:
                if not isinstance(value, step.input_type):
                    raise TypeError(
                        f"Expected {step.input_type.__name__}, got {type(value).__name__}"
                    )
                outcome = step.run(value)
                if not isinstance(outcome, StepResult):
                    raise TypeError("Step must return StepResult")
                if not isinstance(outcome.output, step.output_type):
                    raise TypeError(
                        f"Expected output {step.output_type.__name__}, got {type(outcome.output).__name__}"
                    )
                value = outcome.output
                result.output = value
                result.warnings.extend(outcome.warnings)
                completed = True
                if capture_steps is True or name in captured_names:
                    result.captured_steps[name] = value
            except Exception as exc:
                code = getattr(exc, "code", None)
                details = getattr(exc, "details", None)
                result.error = PipelineError(name, type(exc).__name__, str(exc),
                                             code if isinstance(code, str) else None,
                                             details if isinstance(details, dict) else None)
            result.steps.append(
                StepExecution(
                    name,
                    type(step).__name__,
                    step.version,
                    config,
                    perf_counter() - started,
                    completed,
                )
            )
            if result.error:
                break
        return result
