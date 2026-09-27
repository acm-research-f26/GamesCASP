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
modifier(Actor, attack, 10)  :- enemy_nearby(Actor).
modifier(Actor, attack, 5)   :- enemy_low_health(Actor).
modifier(Actor, attack, -20) :- low_health(Actor).
modifier(Actor, flee, 50)    :- low_health(Actor).
modifier(Actor, wander, 10)  :- not enemy_nearby(Actor).

% Sum a list of numbers
sum_mods([], 0).
sum_mods([H|T], Sum) :-
    sum_mods(T, Rest),
    Sum is H + Rest.

% Calculate final priority
priority(Actor, Action, Priority) :-
    base_priority(Action, Base),
    findall(Modifier, modifier(Actor, Action, Modifier), Modifiers),
    sum_mods(Modifiers, TotalModifier),
    Priority is Base + TotalModifier.

% Helper: is OtherAction NOT a counterexample to Action being good?
% (i.e. either it's the same action, or it doesn't beat Action's priority)
not_worse(Action, Priority, OtherAction) :-
    OtherAction = Action.
not_worse(Action, Priority, OtherAction) :-
    OtherAction \= Action,
    priority(_, OtherAction, OtherPriority),
    OtherPriority =< Priority.

good_action(Actor, Action) :-
    priority(Actor, Action, Priority),
    forall(OtherAction, not_worse(Action, Priority, OtherAction)).

?- good_action(player, Action).