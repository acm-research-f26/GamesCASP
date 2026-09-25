:- use_module(library(scasp)).

age(alice, 24).
age(bob, 10).


adult(X) :- age(X, N), N >= 18.

:-  adult(X), grade(X, kindergarten).