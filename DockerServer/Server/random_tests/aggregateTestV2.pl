% ============================================================
% DOMAIN OF ACTIONS
% ============================================================
action(attack).
action(flee).
action(wander).

% ============================================================
% BASE PRIORITIES
% ============================================================
base_priority(attack, 50).
base_priority(flee, 40).
base_priority(wander, 10).

% ============================================================
% WORLD CONDITIONS
% ============================================================
enemy_nearby(player).
enemy_low_health(player).
low_health(player).

% ============================================================
% PRIORITY MODIFIERS
% ============================================================
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

% ============================================================
% SUM A LIST OF NUMBERS
% ============================================================
sum_list([], 0).

sum_list([H|T], Sum) :-
    sum_list(T, Rest),
    Sum is H + Rest.

% ============================================================
% CALCULATE FINAL PRIORITY
% ============================================================
priority(Actor, Action, Priority) :-
    base_priority(Action, Base),
    findall(
        Modifier,
        modifier(Actor, Action, Modifier),
        Modifiers
    ),
    sum_list(Modifiers, TotalModifier),
    Priority is Base + TotalModifier.

% ============================================================
% DOMINATED / GOOD ACTION  (grounded version)
% ============================================================
dominated(Actor, Action, Priority) :-
    action(OtherAction),                % ground the competitor
    OtherAction \= Action,
    priority(Actor, OtherAction, OtherPriority),
    OtherPriority > Priority.

good_action(Actor, Action) :-
    action(Action),                     % ground Action first
    priority(Actor, Action, Priority),
    not dominated(Actor, Action, Priority).

% ------------------------------------------------------------
% QUERY
% ------------------------------------------------------------
?- good_action(player, Action).