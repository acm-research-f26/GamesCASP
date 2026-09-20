% rules

flies(X) :- 
    bird(X),
    not abnormal(X).

% might not be necessary
-flies(X) :-
    bird(X),
    abnormal(X).

abnormal(X) :-
    bird(X),
    -flies(X).

abnormal(X) :-
    penguin(X).

bird(X) :-
    penguin(X).

% facts
bird(tweety).
bird(kiwi).
penguin(pingu).