% rules

bird(X) :-
    penguin(X).

abnormal(X) :-
    penguin(X).

flies(X) :- 
    bird(X),
    not abnormal(X).

-flies(X) :-
    bird(X),
    abnormal(X).

% facts

bird(tweety).
bird(kiwi).
penguin(pingu).