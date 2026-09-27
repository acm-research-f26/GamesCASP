:- use_module(library(scasp)).

% Base priority for each action
base_priority(attack, 50).
base_priority(flee, 40).
base_priority(wander, 10).


% Conditions
enemy_nearby(player).
enemy_low_health(player).
low_health(player).


% Priority modifiers
modifier(Actor, attack, 10) :-
    enemy_nearby(Actor).

modifier(Actor, attack, 5) :-
    enemy_low_health(Actor).

modifier(Actor, attack, -20) :-
    low_health(Actor).

modifier(Actor, flee, 50) :-
    low_health(Actor).

modifier(Actor, wander, 10) :-
    not enemy_nearby(Actor).


% Sum a list of numbers
sum_list([], 0).

sum_list([H|T], Sum) :-
    sum_list(T, Rest),
    Sum is H + Rest.


% Calculate final priority
priority(Actor, Action, Priority) :-
    base_priority(Action, Base),
    findall(
        Modifier,
        modifier(Actor, Action, Modifier),
        Modifiers
    ),
    sum_list(Modifiers, TotalModifier),
    Priority is Base + TotalModifier.


% Sample query
% ?- priority(player, attack, Priority).


dominated(Actor, Action, Priority) :-
    priority(Actor, OtherAction, OtherPriority),
    Action \= OtherAction,
    OtherPriority > Priority.

good_action(Actor, Action) :-
    priority(Actor, Action, Priority),

    not dominated(Actor, Action, Priority).


% ------------------------------------------------------------
% QUERY
% ------------------------------------------------------------

?- good_action(player, Action).