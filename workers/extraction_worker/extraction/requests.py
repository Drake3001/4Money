"""Explicit request policies, invoked only for cache misses before transport."""


def dispatch_request(body, send, policy=None):
    return send(body) if policy is None else policy(body, send)


def cache_only(body, send):
    raise RuntimeError("Offline replay requires a cached response; network is forbidden")


class BudgetedRequests:
    """Reserve before sending, count failures, record usage even on invalid JSON."""

    def __init__(self, budget, bound):
        self.budget = budget
        self.bound = bound

    def __call__(self, body, send):
        self.budget.reserve(self.bound(body))
        response = send(body)
        self.budget.charge(response)
        return response
