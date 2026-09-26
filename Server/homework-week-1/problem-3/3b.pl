age(alice, 24).
age(bob, 10).
grade(bob, kindergarten).

adult(X) :- age(X, N), N >= 18.

:- adult(X), grade(X, kindergarten).

% why did we even need separate files dawg