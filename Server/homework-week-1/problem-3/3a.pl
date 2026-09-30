age(alice, 24).
age(bob, 10).

% bring this over from 3b
grade(bob, kindergarten).

% if X is an adult, their age is greater than or equal to 18
adult(X) :- age(X, N), N >= 18.

% NO HEAD!!! cuz its an integrity constraint
% this says X cannot be an adult and be in kindergarten at the same time, no matter what
:- adult(X), grade(X, kindergarten).