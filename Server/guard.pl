:- use_module(library(scasp)).

notices(Guard, Player) :- distance(Guard, Player, Distance), vision_range(Guard, Range), Distance =< Range, not hidden(Player), not blocked(Guard, Player).