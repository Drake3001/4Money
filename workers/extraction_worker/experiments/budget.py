"""Shared persisted reservation ledger for bounded model experiments."""

import json
from decimal import Decimal
from pathlib import Path


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")


class Budget:
    def __init__(self, root, limit="0.50", max_calls=120):
        self.root = Path(root)
        if (self.root / "budget.json").exists():
            raise FileExistsError("Budget ledger already exists; use Budget.restore to retain reservations")
        self.limit = Decimal(str(limit))
        if not self.limit.is_finite() or not 0 < self.limit <= Decimal("0.75"):
            raise ValueError("Benchmark reservation budget must be between 0 and USD 0.75")
        if type(max_calls) is not int or not 1 <= max_calls <= 150:
            raise ValueError("Call cap must be 1–150")
        self.max_calls = max_calls
        self.reserved = Decimal(0)
        self.cost = Decimal(0)
        self.calls = 0
        self.missing_cost = 0
        self.persist()

    def persist(self):
        save(self.root / "budget.json", {"budget_usd": str(self.limit), "reserved_usd": str(self.reserved),
             "reported_cost_usd": str(self.cost), "calls": self.calls, "max_calls": self.max_calls,
             "responses_without_cost": self.missing_cost})

    @classmethod
    def restore(cls, root):
        value = json.loads((Path(root) / "budget.json").read_text(encoding="utf-8"))
        instance = cls.__new__(cls)
        instance.root = Path(root)
        instance.limit = Decimal(value["budget_usd"])
        instance.max_calls = value["max_calls"]
        instance.reserved = Decimal(value["reserved_usd"])
        instance.cost = Decimal(value["reported_cost_usd"])
        instance.calls = value["calls"]
        instance.missing_cost = value["responses_without_cost"]
        if not (all(v.is_finite() for v in (instance.reserved, instance.limit, instance.cost))
                and 0 < instance.limit <= Decimal("0.75") and 0 <= instance.reserved <= instance.limit
                and instance.cost >= 0 and type(instance.calls) is int and type(instance.max_calls) is int
                and 1 <= instance.max_calls <= 150 and 0 <= instance.calls <= instance.max_calls
                and type(instance.missing_cost) is int and 0 <= instance.missing_cost <= instance.calls):
            raise ValueError("Invalid persisted budget")
        return instance

    def reserve(self, amount):
        amount = Decimal(str(amount))
        if not amount.is_finite() or amount < 0:
            raise ValueError("Reservation must be finite and nonnegative")
        if self.calls >= self.max_calls or self.reserved + amount > self.limit:
            raise RuntimeError("Benchmark budget/call cap reached before request")
        self.reserved += amount
        self.calls += 1
        self.persist()  # A timed out call is still counted and reserved.

    def charge(self, response):
        value = (response.get("usage") or {}).get("cost")
        if value is None:
            self.missing_cost += 1
        else:
            cost = Decimal(str(value))
            if not cost.is_finite() or cost < 0:
                raise ValueError("Invalid cost returned by provider")
            self.cost += cost
        self.persist()



class SettledBudget:
    """Sequential study budget: billed responses release unused reservation headroom.

    Failed/unknown-cost attempts retain their full bound. The ledger records every
    attempt and cumulative bounds separately; settlement never resets call counts.
    """

    def __init__(self, root, limit="0.45", max_calls=48):
        self.root = Path(root)
        if (self.root / "budget.json").exists():
            raise FileExistsError("Budget ledger already exists; restore it explicitly")
        self.limit = Decimal(str(limit))
        if not self.limit.is_finite() or not 0 < self.limit <= Decimal("0.75"):
            raise ValueError("Study budget must be between 0 and USD 0.75")
        if type(max_calls) is not int or not 1 <= max_calls <= 150:
            raise ValueError("Call cap must be 1–150")
        self.max_calls = max_calls
        self.attempts = []
        self.persist()

    @property
    def calls(self):
        return len(self.attempts)

    @property
    def cost(self):
        return sum((Decimal(a["cost"]) for a in self.attempts if a.get("cost") is not None), Decimal(0))

    @property
    def reserved(self):
        return sum((Decimal(a["cost"] if a.get("cost") is not None else a["bound"]) for a in self.attempts), Decimal(0))

    def persist(self):
        save(self.root / "budget.json", {"mode": "settle_known_costs", "budget_usd": str(self.limit),
             "max_calls": self.max_calls, "calls": self.calls, "reported_cost_usd": str(self.cost),
             "reserved_usd": str(self.reserved), "cumulative_bounds_usd": str(sum(
                 (Decimal(a["bound"]) for a in self.attempts), Decimal(0))), "attempts": self.attempts})

    def reserve(self, amount):
        amount = Decimal(str(amount))
        if not amount.is_finite() or amount < 0:
            raise ValueError("Reservation must be finite and nonnegative")
        if self.calls >= self.max_calls or self.reserved + amount > self.limit:
            raise RuntimeError("Study budget/call cap reached before request")
        self.attempts.append({"number": self.calls + 1, "bound": str(amount), "status": "unsettled"})
        self.persist()

    def charge(self, response):
        if not self.attempts or self.attempts[-1]["status"] != "unsettled":
            raise RuntimeError("No uncharged current attempt")
        attempt = self.attempts[-1]
        value = (response.get("usage") or {}).get("cost")
        if value is None:
            attempt["status"] = "unknown_cost"
            self.persist()
            return
        cost = Decimal(str(value))
        if not cost.is_finite() or cost < 0:
            raise ValueError("Invalid cost returned by provider")
        attempt.update(status="settled", cost=str(cost))
        self.persist()
        if cost > Decimal(attempt["bound"]):
            raise RuntimeError("Provider cost exceeded reservation; study stopped")

    @classmethod
    def restore(cls, root):
        raw = json.loads((Path(root) / "budget.json").read_text(encoding="utf-8"))
        if raw.get("mode") != "settle_known_costs":
            raise ValueError("Not a settled study ledger")
        instance = cls.__new__(cls)
        instance.root, instance.limit, instance.max_calls = Path(root), Decimal(raw["budget_usd"]), raw["max_calls"]
        instance.attempts = raw["attempts"]
        if not instance.limit.is_finite() or not 0 < instance.limit <= Decimal("0.75"):
            raise ValueError("Invalid study limit")
        if type(instance.max_calls) is not int or not 1 <= instance.max_calls <= 150 or instance.calls > instance.max_calls:
            raise ValueError("Invalid study call cap")
        for number, attempt in enumerate(instance.attempts, 1):
            bound = Decimal(attempt["bound"])
            cost = Decimal(attempt["cost"]) if attempt.get("cost") is not None else None
            if (attempt["number"] != number or not bound.is_finite() or bound < 0
                    or attempt["status"] not in ("unsettled", "unknown_cost", "settled")
                    or (attempt["status"] == "settled") != (cost is not None)
                    or cost is not None and (not cost.is_finite() or cost < 0)):
                raise ValueError("Invalid study attempt")
        if str(instance.reserved) != raw["reserved_usd"] or str(instance.cost) != raw["reported_cost_usd"]:
            raise ValueError("Inconsistent study ledger")
        return instance
