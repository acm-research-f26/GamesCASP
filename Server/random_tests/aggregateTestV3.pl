:- use_module(library(scasp)).

base_priority(attack, 50).
base_priority(flee, 40).
base_priority(wander, 10).

action(attack).
action(flee).
action(wander).

enemy_nearby(player).
enemy_low_health(player).
low_health(player).

% ---- fixed set of modifier "slots" (grounds the *sum*, not just the action) ----
condition_key(enemy_nearby_bonus).
condition_key(low_hp_enemy_bonus).
condition_key(self_low_hp_penalty).
condition_key(self_low_hp_bonus).
condition_key(no_enemy_bonus).

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

contributes(Actor, Action, Key, 0) :-
    condition_key(Key),
    action(Action),
    not has_specific_contribution(Actor, Action, Key).

has_specific_contribution(Actor, Action, Key) :-
    contributes(Actor, Action, Key, _).

all_condition_keys([enemy_nearby_bonus, low_hp_enemy_bonus,
                     self_low_hp_penalty, self_low_hp_bonus,
                     no_enemy_bonus]).

sum_over_keys(_, _, [], 0).
sum_over_keys(Actor, Action, [K|Ks], Total) :-
    contributes(Actor, Action, K, V),
    sum_over_keys(Actor, Action, Ks, Rest),
    Total is V + Rest.

% ---- priority/3: no findall anywhere in its definition ----
priority(Actor, Action, P) :-
    action(Action),
    base_priority(Action, Base),
    all_condition_keys(Keys),
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