:- use_module(library(scasp)).

action(attack).
action(flee).
action(wander).

base_priority(attack, 50).
base_priority(flee, 40).
base_priority(wander, 10).

enemy_nearby(player).
enemy_low_health(player).
low_health(player).

% ---- which keys actually matter to each action (fact, not negation) ----
relevant_keys(attack, [enemy_nearby_bonus, low_hp_enemy_bonus, self_low_hp_penalty]).
relevant_keys(flee,   [self_low_hp_bonus]).
relevant_keys(wander, [no_enemy_bonus]).

% ---- contributions: only ever defined for relevant pairs, no fallback ----
contributes(Actor, attack, enemy_nearby_bonus, 10) :- enemy_nearby(Actor).
contributes(Actor, attack, enemy_nearby_bonus, 0)  :- not enemy_nearby(Actor).

contributes(Actor, attack, low_hp_enemy_bonus, 5)  :- enemy_low_health(Actor).
contributes(Actor, attack, low_hp_enemy_bonus, 0)  :- not enemy_low_health(Actor).

contributes(Actor, attack, self_low_hp_penalty, -20) :- low_health(Actor).
contributes(Actor, attack, self_low_hp_penalty, 0)   :- not low_health(Actor).

contributes(Actor, flee, self_low_hp_bonus, 50) :- low_health(Actor).
contributes(Actor, flee, self_low_hp_bonus, 0)  :- not low_health(Actor).

contributes(Actor, wander, no_enemy_bonus, 10) :- not enemy_nearby(Actor).
contributes(Actor, wander, no_enemy_bonus, 0)  :- enemy_nearby(Actor).

% ---- sum only over the keys relevant to THIS action ----
sum_over_keys(_, _, [], 0).
sum_over_keys(Actor, Action, [K|Ks], Total) :-
    contributes(Actor, Action, K, V),
    sum_over_keys(Actor, Action, Ks, Rest),
    Total is V + Rest.

priority(Actor, Action, P) :-
    action(Action),
    base_priority(Action, Base),
    relevant_keys(Action, Keys),
    sum_over_keys(Actor, Action, Keys, Sum),
    P is Base + Sum.

dominated(Actor, Action, Priority) :-
    action(OtherAction),
    OtherAction \= Action,
    priority(Actor, OtherAction, OtherPriority),
    OtherPriority > Priority.

good_action(Actor, Action) :-
    action(Action),
    priority(Actor, Action, Priority),
    not dominated(Actor, Action, Priority).

?- good_action(player, Action).