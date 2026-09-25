:- use_module(library(scasp)).

parent(alice, bob).
parent(bob, carol).
parent(carol, dave).

grandparent(X, Y) :- parent(X, Z), parent(Z, Y).
ancestor(X, Y) :- parent(X, Y).
ancestor(X, Y) :- parent(X, Z), ancestor(Z, Y).