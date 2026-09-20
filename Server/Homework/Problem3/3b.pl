age(alice, 24).
age(bob, 10).


adult(X) :- age(X, N), N >= 18.

% in addition from 3a.pl
grade(bob, kindergarten).
adult(bob).
