FROM python:3.12-slim

COPY --from=ghcr.io/astral-sh/uv:0.12.23 /uv /uvx /bin/

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    UV_PYTHON_DOWNLOADS=never

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 libgomp1 \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 worker

WORKDIR /app
COPY pyproject.toml uv.lock ./
COPY extraction/ ./extraction/
COPY experiments/ ./experiments/
COPY main.py api.py api_models.py api_errors.py api_limits.py ./
RUN uv sync --frozen --no-dev \
    && mkdir -p /app/.cache \
    && chown worker:worker /app/.cache

USER worker
EXPOSE 8000
CMD [".venv/bin/uvicorn", "api:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "1"]
