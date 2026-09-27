:- use_module(library(clpfd)).

base_priority(attack, 60, 90).
base_priority(flee,   50, 100).
base_priority(heal,   20, 40).
base_priority(wander,  0, 10).

enemy_nearby(player).
enemy_low_health(player).
low_health(player).

is_valid(Actor, attack) :-
    enemy_nearby(Actor).

is_valid(Actor, flee) :-
    low_health(Actor).

is_valid(Actor, heal) :-
    low_health(Actor).

is_valid(_, wander).


% All applicable modifiers are zero for this test.

modifier(Actor, attack, 0, 0) :-
    enemy_nearby(Actor).

modifier(Actor, attack, 0, 0) :-
    enemy_low_health(Actor).

modifier(Actor, attack, 0, 0) :-
    low_health(Actor).

modifier(Actor, flee, 0, 0) :-
    low_health(Actor).

modifier(Actor, heal, 0, 0) :-
    low_health(Actor).

modifier(Actor, wander, 0, 0) :-
    not enemy_nearby(Actor).


sum_list([], 0).

sum_list([H|T], Sum) :-
    sum_list(T, Rest),
    Sum #= H + Rest.


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


% A dominates B if both bounds are at least as good
% and at least one is strictly better.

dominates([LowerA, UpperA], [LowerB, UpperB]) :-
    LowerA #>= LowerB,
    UpperA #>= UpperB,
    LowerA #> LowerB.

dominates([LowerA, UpperA], [LowerB, UpperB]) :-
    LowerA #>= LowerB,
    UpperA #>= UpperB,
    UpperA #> UpperB.


valid_priority(Actor, Action, Lower, Upper) :-
    is_valid(Actor, Action),
    priority(Actor, Action, Lower, Upper).


collect_actions(Actor, Actions) :-
    findall([Action, Lower, Upper],
            valid_priority(Actor, Action, Lower, Upper),
            Actions).


count_dominators(_, [], 0).

% Same action: skip it.
count_dominators([Action, Lower, Upper],
                 [[OtherAction, _, _]|Rest],
                 Count) :-
    Action = OtherAction,
    count_dominators([Action, Lower, Upper],
                     Rest,
                     Count).

% Other action dominates this candidate.
count_dominators([Action, Lower, Upper],
                 [[OtherAction, OtherLower, OtherUpper]|Rest],
                 Count) :-
    Action \= OtherAction,
    dominates([OtherLower, OtherUpper],
              [Lower, Upper]),
    count_dominators([Action, Lower, Upper],
                     Rest,
                     RestCount),
    Count #= RestCount + 1.

% Other action has a lower lower-bound,
% so it cannot dominate.
count_dominators([Action, Lower, Upper],
                 [[OtherAction, OtherLower, _]|Rest],
                 Count) :-
    Action \= OtherAction,
    OtherLower #< Lower,
    count_dominators([Action, Lower, Upper],
                     Rest,
                     Count).

% Same lower-bound, but other upper-bound is
% not strictly greater.
count_dominators([Action, Lower, Upper],
                 [[OtherAction, OtherLower, OtherUpper]|Rest],
                 Count) :-
    Action \= OtherAction,
    OtherLower #= Lower,
    OtherUpper #=< Upper,
    count_dominators([Action, Lower, Upper],
                     Rest,
                     Count).

% Other action has a higher lower-bound but
% a lower upper-bound, so the intervals cross.
count_dominators([Action, Lower, Upper],
                 [[OtherAction, OtherLower, OtherUpper]|Rest],
                 Count) :-
    Action \= OtherAction,
    OtherLower #> Lower,
    OtherUpper #< Upper,
    count_dominators([Action, Lower, Upper],
                     Rest,
                     Count).

filter_non_dominated([], _, []).

filter_non_dominated([Candidate|Rest],
                     AllActions,
                     [Candidate|FilteredRest]) :-
    count_dominators(Candidate, AllActions, 0),
    filter_non_dominated(Rest,
                         AllActions,
                         FilteredRest).

filter_non_dominated([Candidate|Rest],
                     AllActions,
                     FilteredRest) :-
    count_dominators(Candidate, AllActions, Count),
    Count #> 0,
    filter_non_dominated(Rest,
                         AllActions,
                         FilteredRest).


get_action(Action, [[Action, _, _]|_]).

get_action(Action, [_|Rest]) :-
    get_action(Action, Rest).


non_dominated(Actor, Action) :-
    collect_actions(Actor, Actions),
    filter_non_dominated(Actions,
                         Actions,
                         BestActions),
    get_action(Action, BestActions).


?- non_dominated(player, Action).