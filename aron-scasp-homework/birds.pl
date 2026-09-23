

bird(tweety).
bird(pingu).
bird(kiwi).

penguin(pingu).

abnormal(X) :-
    penguin(X).

flies(X) :-
    bird(X),
    not abnormal(X).

-flies(pingu).