% doors.pl code provided from the text doc

%%%%%%%%%%%%%%%%%%%%%%%%%%%%
%%%%%%%%%%%%%%%%%%%%%%%%%%%%

% EXPLANATION FOR PROBLEM 5:
% Door2 does not open because the sealed condition makes it blocked. 
% That has no relation with -safe though.
% This is proven since -safe only comes from alarm_triggered condition.

%%%%%%%%%%%%%%%%%%%%%%%%%%%%
%%%%%%%%%%%%%%%%%%%%%%%%%%%%

room(entrance).
room(hall).
room(vault).
room(treasure).
room(storage).

connects(entrance, hall, door1).
connects(hall, vault, door2).
connects(vault, treasure, door3).
connects(entrance, storage, door5).

has_key(player, gold_key).
has_key(player, silver_key).
has_key(player, bronze_key).

locked(door1).
locked(door2).
locked(door5).

sealed(door2).
alarm_triggered(door5).

unlocks(gold_key, door1).
unlocks(silver_key, door2).
unlocks(bronze_key, door5).

blocked(D) :- sealed(D).

opens(D) :- locked(D), has_key(player, K), unlocks(K, D), not alarm_triggered(D), not blocked(D).
opens(D) :- not locked(D).

reachable(entrance).
reachable(R2) :- connects(R1, R2, D), reachable(R1), opens(D).

-safe(D) :- alarm_triggered(D).