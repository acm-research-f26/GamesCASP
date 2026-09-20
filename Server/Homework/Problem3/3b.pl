age(alice, 24).
age(bob, 10).


adult(X) :- age(X, N), N >= 18.

% forbid adults from being in kindergarten
:- adult(X), grade(X, kindergarten).

% in addition from 3a.pl
grade(bob, kindergarten).
adult(bob). % no longer true due to integrity rule
