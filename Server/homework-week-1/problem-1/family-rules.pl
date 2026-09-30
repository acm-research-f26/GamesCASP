% x is the grandparent of z if x is the parent of y, and y is the parent of z!
grandparent(X, Z) :- parent(X, Y), parent(Y, Z).

% "a parent is an ancestor"
ancestor(X, Y) :- parent(X, Y).

% "and a parent of an ancestor is an ancestor"
% sooooo x is the ancestor of z if x is the parent of y, and y is an ancestor of z
ancestor(X, Z) :- parent(X, Y), ancestor(Y, Z).

