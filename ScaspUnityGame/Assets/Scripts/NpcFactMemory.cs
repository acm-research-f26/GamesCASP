using System;
using System.Collections.Generic;
using System.Globalization;

// Keeps the stored facts one NPC already learned so a new strategy can get them after a role change
// It follows the same rules as the Python fact file in StoredFacts.py
// Permanent facts are kept once, a mutable fact only keeps its latest version, transient facts are never kept
public sealed class NpcFactMemory
{
    private sealed class Entry
    {
        public Fact Fact;
        // The strategy that already has this fact, so we dont send it to the same one twice
        public IAgentStrategy ReceivedBy;
    }

    private readonly List<Entry> entries = new List<Entry>();

    public int Count => entries.Count;

    // Get the remembered facts in the order they were learned
    public IReadOnlyList<Fact> Facts
    {
        get
        {
            var facts = new List<Fact>(entries.Count);
            foreach (Entry entry in entries)
                facts.Add(entry.Fact);
            return facts.AsReadOnly();
        }
    }

    // Remember a fact that a strategy just stored successfully
    // Returns false for transient facts since those get refreshed every decision anyway
    public bool Remember(Fact fact, IAgentStrategy receivedBy)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        if (fact.Lifetime == FactLifetime.Transient)
            return false;

        string key = Key(fact);
        int existing = entries.FindIndex(entry => Key(entry.Fact) == key);

        // The same permanent fact again is a duplicate, just keep the first one
        if (existing >= 0 && fact.Lifetime == FactLifetime.Permanent)
            return true;

        // A newer mutable fact replaces the old one, same as Python does
        if (existing >= 0)
            entries.RemoveAt(existing);

        entries.Add(new Entry { Fact = fact, ReceivedBy = receivedBy });
        return true;
    }

    // Give the new strategy every fact it doesnt have yet
    // Failures get reported to onFailure instead of being ignored, and that fact can be tried again later
    public int ReplayInto(IAgentStrategy strategy, Action<Fact, Exception> onFailure)
    {
        if (strategy == null) throw new ArgumentNullException(nameof(strategy));

        int replayed = 0;
        foreach (Entry entry in entries)
        {
            // Skip facts this strategy already got so we dont write them twice
            if (ReferenceEquals(entry.ReceivedBy, strategy))
                continue;

            try
            {
                strategy.StoreFact(entry.Fact);
                entry.ReceivedBy = strategy;
                replayed++;
            }
            catch (Exception exception)
            {
                onFailure?.Invoke(entry.Fact, exception);
            }
        }
        return replayed;
    }

    // Matches the Python rules, mutable facts are replaced by type and subject,
    // permanent facts are only duplicates if everything is the same
    private static string Key(Fact fact)
    {
        if (fact.Lifetime == FactLifetime.Mutable)
            return $"mutable|{fact.Type}|{fact.SubjectId}";

        string distance = fact.Distance.HasValue ? fact.Distance.Value.ToString("R", CultureInfo.InvariantCulture) : "";
        return $"permanent|{fact.Type}|{fact.SubjectId}|{fact.TargetId ?? ""}|{distance}";
    }
}
