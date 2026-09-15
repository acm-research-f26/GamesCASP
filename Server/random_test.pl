:- use_module(library(scasp)).

bird(tweety).
bird(polly).
fish(nemo).
mammal(rex).
-bird(X) :- fish(X).
-bird(X) :- mammal(X).