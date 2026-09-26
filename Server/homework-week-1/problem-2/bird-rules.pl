bird(X) :- penguin(X). % all birds are penguins

abnormal(X) :- penguin(X). % but penguins are *abnormal* birds...

flies(X) :- bird(X), not abnormal(X).

% we gotta explicitly define what -flies actually means since scasp cant infer that itself
-flies(X) :- abnormal(X).