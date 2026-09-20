notices(X, Y) :-
    distance(X, Y, D),
    vision_range(X, R),
    R >= D,
    not blocked(X, Y),
    not hidden(Y).