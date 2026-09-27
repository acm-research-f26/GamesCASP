:- use_module(library(scasp)).

% Explicit domain of actions
action(attack).
action(flee).
action(wander).

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
not_worse(Actor, Action, Priority, OtherAction) :-
    not action(OtherAction).            % irrelevant binding: vacuously fine
not_worse(Actor, Action, Priority, OtherAction) :-
    action(OtherAction),
    OtherAction = Action.                % same action: fine
not_worse(Actor, Action, Priority, OtherAction) :-
    action(OtherAction),
    OtherAction \= Action,
    priority(Actor, OtherAction, OtherPriority),
    OtherPriority =< Priority.            % different action: must not beat us

good_action(Actor, Action) :-
    priority(Actor, Action, Priority),
    forall(OtherAction, not_worse(Actor, Action, Priority, OtherAction)).

?- good_action(player, Action).