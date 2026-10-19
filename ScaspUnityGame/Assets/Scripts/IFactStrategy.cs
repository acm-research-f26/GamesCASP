// Implement on the behavior-tree and s(CASP) strategy components.
// Each strategy instance belongs to one agent. Transient facts use a separate query path.
public interface IFactStrategy
{
    void StoreFact(Fact fact);
}
