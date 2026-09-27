% ============================================================
% GamesCASP - Interval-Based Action Selection
% ============================================================

:- use_module(library(clpfd)).


% ------------------------------------------------------------
% BASE PRIORITIES
%
% base_priority(Action, LowerBound, UpperBound).
% ------------------------------------------------------------

base_priority(attack, 40, 70).
base_priority(flee,   30, 60).
base_priority(heal,   20, 50).
base_priority(wander,  0, 20).


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
%
% modifier(Actor, Action, LowerChange, UpperChange).
%
% Each applicable rule independently modifies the interval.
% ------------------------------------------------------------

modifier(Actor, attack, 10, 10) :-
    enemy_nearby(Actor).

modifier(Actor, attack, 10, 10) :-
    enemy_low_health(Actor).

modifier(Actor, attack, -20, -20) :-
    low_health(Actor).

modifier(Actor, flee, 30, 30) :-
    low_health(Actor).

modifier(Actor, heal, 20, 20) :-
    low_health(Actor).

modifier(Actor, wander, 10, 10) :-
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
%
% Lower and Upper are CLP(FD) variables constrained to the
% resulting priority interval.
% ------------------------------------------------------------

priority(Actor, Action, Lower, Upper) :-

    base_priority(Action, BaseLower, BaseUpper),

    findall(
        LowerChange,
        modifier(Actor, Action, LowerChange, _),
        LowerChanges
    ),

    findall(
        UpperChange,
        modifier(Actor, Action, _, UpperChange),
        UpperChanges
    ),

    sum_list(LowerChanges, TotalLowerChange),
    sum_list(UpperChanges, TotalUpperChange),

    Lower #= BaseLower + TotalLowerChange,
    Upper #= BaseUpper + TotalUpperChange,

    Lower #=< Upper.


% ------------------------------------------------------------
% INTERVAL DOMINANCE
%
% Action A dominates Action B if:
%
%     A.lower >= B.lower
%     A.upper >= B.upper
%
% and at least one bound is strictly greater.
% ------------------------------------------------------------

dominates(Actor, ActionA, ActionB) :-

    priority(Actor, ActionA, AL, AU),
    priority(Actor, ActionB, BL, BU),

    AL #>= BL,
    AU #>= BU,
    AL #> BL.


dominates(Actor, ActionA, ActionB) :-

    priority(Actor, ActionA, AL, AU),
    priority(Actor, ActionB, BL, BU),

    AL #>= BL,
    AU #>= BU,
    AU #> BU.

% ------------------------------------------------------------
% NON-DOMINATED ACTION
%
% An action is non-dominated if:
%
%   1. It is valid.
%   2. Its priority interval is consistent.
%   3. No other valid action dominates it.
%
% Prolog backtracking returns every non-dominated action.
% ------------------------------------------------------------


dominated(Actor, Action) :-
    is_valid(Actor, OtherAction),
    Action \= OtherAction,
    dominates(Actor, OtherAction, Action).

non_dominated(Actor, Action) :-

    is_valid(Actor, Action),

    priority(Actor, Action, _, _),

    not dominated(Actor, Action).


% ------------------------------------------------------------
% QUERY
% ------------------------------------------------------------

?- non_dominated(player, Action).