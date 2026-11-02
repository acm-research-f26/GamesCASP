"""Validated agent knowledge persisted alongside the legacy game-session facts."""

from dataclasses import dataclass
import math
import re


def require_atom(value, field):
    if not isinstance(value, str) or re.fullmatch(r"[a-z][A-Za-z0-9_]*", value) is None:
        raise ValueError(f"{field} must be a plain Prolog atom")
    return value


# Witnessed events happened and are never superseded; subject is the attacker, target the victim.
WITNESSED_EVENT_TYPES = {"zombie_attack_witnessed", "bite_attempt_witnessed", "bite_witnessed"}

# Exactly the canonical clauses to_prolog() writes.
_ATOM = r"[a-z][A-Za-z0-9_]*"
_STORED_CLAUSE = re.compile(
    rf"stored_fact\(({_ATOM}), (?:mutable|permanent), {_ATOM}, {_ATOM}, {_ATOM}, "
    r"(?:none|[0-9][0-9.e+-]*)\)\."
)


@dataclass(frozen=True)
class StoredFact:
    actor: str
    lifetime: str
    type: str
    subject: str
    target: str
    distance: str

    @classmethod
    def from_payload(cls, actor, payload):
        if not isinstance(payload, dict):
            raise ValueError("Each fact must be a JSON object")

        types = {
            "LeverActivated": "lever_activated",
            "AgentAttacked": "agent_attacked",
            "ZombieSeen": "zombie_seen",
            "ZombieAttackWitnessed": "zombie_attack_witnessed",
            "BiteAttemptWitnessed": "bite_attempt_witnessed",
            "BiteWitnessed": "bite_witnessed",
        }
        fact_type = payload.get("type")
        lifetime = payload.get("lifetime")
        if not isinstance(fact_type, str) or fact_type not in types:
            raise ValueError("Unknown fact type")
        if lifetime not in ("Mutable", "Permanent"):
            raise ValueError("Stored facts must have Mutable or Permanent lifetime")
        if types[fact_type] in WITNESSED_EVENT_TYPES and lifetime != "Permanent":
            raise ValueError("Witnessed events must have Permanent lifetime")

        subject = require_atom(payload.get("subject_id"), "subject_id")
        target = payload.get("target_id")
        if types[fact_type] in WITNESSED_EVENT_TYPES and target in (None, ""):
            raise ValueError("Witnessed events need the victim as target_id")
        target = "none" if target in (None, "") else require_atom(target, "target_id")
        has_distance = payload.get("has_distance", False)
        if not isinstance(has_distance, bool):
            raise ValueError("has_distance must be a boolean")
        distance = "none"
        if has_distance:
            value = payload.get("distance")
            if (isinstance(value, bool) or not isinstance(value, (int, float))
                    or value < 0 or value > 1.7976931348623157e308
                    or not math.isfinite(value)):
                raise ValueError("distance must be a finite nonnegative number")
            distance = str(value)

        return cls(actor, lifetime.lower(), types[fact_type], subject, target, distance)

    @property
    def mutable_prefix(self):
        # Target and distance are the replaceable values for this subject's fact.
        return f"stored_fact({self.actor}, mutable, {self.type}, {self.subject}, "

    def to_prolog(self):
        return (f"stored_fact({self.actor}, {self.lifetime}, {self.type}, "
                f"{self.subject}, {self.target}, {self.distance}).\n")


def stored_facts_for_actor(text, actor):
    """One actor's own stored clauses, in file order, for its decision snapshot.

    Other actors' knowledge and legacy session facts are left out, so each NPC
    reasons only over what it observed itself.
    """
    require_atom(actor, "actor")
    clauses = []
    for line in text.splitlines():
        match = _STORED_CLAUSE.fullmatch(line.strip())
        if match is not None and match.group(1) == actor:
            clauses.append(line.strip() + "\n")
    return clauses


def update_stored_facts(path, facts):
    """Caller holds facts_lock. Replays are safe; legacy clauses stay untouched."""
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    for fact in facts:
        if fact.lifetime == "mutable":
            lines = [line for line in lines if not line.startswith(fact.mutable_prefix)]
        clause = fact.to_prolog()
        if clause not in lines:
            if lines and not lines[-1].endswith("\n"):
                lines[-1] += "\n"
            lines.append(clause)

    # Replace only after the complete new file is written successfully.
    temporary = path.with_suffix(".pl.tmp")
    try:
        temporary.write_text("".join(lines), encoding="utf-8")
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)
