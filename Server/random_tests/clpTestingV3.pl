% ============================================================
% GamesCASP - Interval-Based Action Selection
% (no member/2, fully ground under negation)
% ============================================================

:- use_module(library(clpfd)).


% ------------------------------------------------------------
% BASE PRIORITIES  (two incomparable non-dominated actions)
% ------------------------------------------------------------
base_priority(attack, 40, 120).   % lower lower, much higher upper
base_priority(flee,   70,  85).   % higher lower, lower upper
base_priority(heal,   20,  45).
base_priority(wander,  0,  25).

% ------------------------------------------------------------
% WORLD STATE
% ------------------------------------------------------------
enemy_nearby(player).
enemy_low_health(player).
low_health(player).


% ------------------------------------------------------------
% VALID ACTIONS
% ------------------------------------------------------------
is_valid(Actor, attack) :-
    enemy_nearby(Actor).

is_valid(Actor, flee) :-
    low_health(Actor).

is_valid(Actor, heal) :-
    low_health(Actor).

is_valid(_, wander).


% ------------------------------------------------------------
% PRIORITY MODIFIERS
% ------------------------------------------------------------
modifier(Actor, attack, 5, 5) :-
    enemy_nearby(Actor).

modifier(Actor, attack, 5, 5) :-
    enemy_low_health(Actor).

modifier(Actor, attack, -15, -15) :-
    low_health(Actor).

modifier(Actor, flee, 10, 10) :-
    low_health(Actor).

modifier(Actor, heal, 10, 10) :-
    low_health(Actor).

modifier(Actor, wander, 5, 5) :-
    not enemy_nearby(Actor).


% ------------------------------------------------------------
% SUM MODIFIERS
% ------------------------------------------------------------
sum_list([], 0).

sum_list([H|T], Sum) :-
    sum_list(T, Rest),
    Sum #= H + Rest.


% ------------------------------------------------------------
% FINAL PRIORITY INTERVAL
% ------------------------------------------------------------
priority(Actor, Action, Lower, Upper) :-
    base_priority(Action, BaseLower, BaseUpper),

    findall(LowerChange,
            modifier(Actor, Action, LowerChange, _),
            LowerChanges),

    findall(UpperChange,
            modifier(Actor, Action, _, UpperChange),
            UpperChanges),

    sum_list(LowerChanges, TotalLowerChange),
    sum_list(UpperChanges, TotalUpperChange),

    Lower #= BaseLower + TotalLowerChange,
    Upper #= BaseUpper + TotalUpperChange,
    Lower #=< Upper.


% ------------------------------------------------------------
% HELPER
% ------------------------------------------------------------
valid_priority(Actor, Action, Lower, Upper) :-
    is_valid(Actor, Action),
    priority(Actor, Action, Lower, Upper).


% ------------------------------------------------------------
% COLLECT ALL VALID PRIORITY INTERVALS
% ------------------------------------------------------------
all_priorities(Actor, Priorities) :-
    findall(
        [Action, Lower, Upper],
        valid_priority(Actor, Action, Lower, Upper),
        Priorities
    ).


% ------------------------------------------------------------
% INTERVAL DOMINANCE
% ------------------------------------------------------------
dominates_interval([_, AL, AU], [_, BL, BU]) :-
    AL #>= BL,
    AU #>= BU,
    AL #> BL.

dominates_interval([_, AL, AU], [_, BL, BU]) :-
    AL #>= BL,
    AU #>= BU,
    AU #> BU.


% ------------------------------------------------------------
% LOOK UP THE INTERVAL OF A GIVEN ACTION (pure recursion)
% ------------------------------------------------------------
lookup(Action, [[Action, L, U]|_], L, U).
lookup(Action, [_|Rest], L, U) :-
    lookup(Action, Rest, L, U).


% ------------------------------------------------------------
% DOES ANY OTHER ACTION DOMINATE THIS ONE? (pure recursion)
% ------------------------------------------------------------
some_dominates(Action, [], _) :- fail.          % never succeeds
some_dominates(Action, [[Other, OL, OU]|Rest], MyInterval) :-
    Other \= Action,
    dominates_interval([Other, OL, OU], MyInterval).
some_dominates(Action, [_|Rest], MyInterval) :-
    some_dominates(Action, Rest, MyInterval).


is_dominated(Action, Priorities) :-
    lookup(Action, Priorities, L, U),
    some_dominates(Action, Priorities, [Action, L, U]).


% ------------------------------------------------------------
% NON-DOMINATED ACTION
% ------------------------------------------------------------
non_dominated(Actor, Action) :-
    all_priorities(Actor, Priorities),
    lookup(Action, Priorities, _, _),        % still ground
    not is_dominated(Action, Priorities).    % pure ground negation


% ------------------------------------------------------------
% QUERY
% ------------------------------------------------------------
?- non_dominated(player, Action).