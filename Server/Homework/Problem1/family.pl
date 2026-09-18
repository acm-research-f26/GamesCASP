% parent (Parent, Child)

parent(alice, bob).
parent(bob, carol).


grandparent(X, Z) :- parent(X, Y), parent(Y, Z).

ancestor(X, Y) :- parent(X, Y).
ancestor(X, Z) :- 
    parent(X, Y),
    ancestor(Y, Z).