
age(alice, 24).

age(bob, 10).

adult(X) :-
    age(X, N),
    N >= 18.

% Integrity constraint, there is no head.
% There must not be a value for x that 
% is both an adult and in kindergarten
:- adult(X), grade(X, kindergarten).