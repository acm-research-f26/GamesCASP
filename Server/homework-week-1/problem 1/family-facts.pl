% rules define what's allowed, facts define what you know
% gud practice for both to be separate files

% parents?
parent(alice, bob). % alice is bob's parent
parent(bob, carol).
parent(bob, dave). % alice has to be an ancestor to dave: alice is bob's parent, bob is dave's parent, thus alice is dave's ancestor

