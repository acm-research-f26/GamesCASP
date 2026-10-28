using System;
using System.Text.RegularExpressions;

// Transport fields live here so Fact remains independent of the s(CASP) backend.
[Serializable]
public sealed class ScaspFactPayload
{
    public string type;
    public string lifetime;
    public string subject_id;
    public string target_id;
    public bool has_distance;
    public float distance;

    public static ScaspFactPayload FromFact(Fact fact)
    {
        if (fact == null)
            throw new ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            throw new ArgumentException("Transient facts must use NPC snapshots, not storage.", nameof(fact));

        ValidateAtom(fact.SubjectId, nameof(fact.SubjectId));
        if (!string.IsNullOrEmpty(fact.TargetId))
            ValidateAtom(fact.TargetId, nameof(fact.TargetId));

        return new ScaspFactPayload
        {
            type = fact.Type.ToString(),
            lifetime = fact.Lifetime.ToString(),
            subject_id = fact.SubjectId,
            target_id = fact.TargetId ?? "",
            has_distance = fact.Distance.HasValue,
            distance = fact.Distance.GetValueOrDefault()
        };
    }

    public static void ValidateAtom(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"^[a-z][A-Za-z0-9_]*\z"))
            throw new ArgumentException("ID must be a plain Prolog atom.", parameterName);
    }
}
