:- use_module(library(scasp)).

% ---- Actions & base priorities (add as many as you want) ----
base_priority(attack, 50).
base_priority(flee,   40).
base_priority(wander, 10).

% ---- The fixed set of modifier "slots" your game logic knows about ----
% This grows when you add a new *kind* of condition, not when you add
% a new action -- so it stays small and stable even with many actions.
condition_key(enemy_nearby_bonus).
condition_key(low_hp_enemy_bonus).
condition_key(self_low_hp_penalty).
condition_key(self_low_hp_bonus).
condition_key(no_enemy_bonus).

% ---- World state ----
enemy_nearby(player).
enemy_low_health(player).
low_health(player).

% ---- Each modifier: contributes(Actor, Action, Key, Value) ----
% Positive case + explicit negative (0) case. Ordinary clauses with
% `not` on a *ground* condition -- perfectly dualizable, no meta-predicates.
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

% Generic fallback: any (Actor,Action,Key) pair you *didn't* bother
% writing a specific contributes/4 clause for defaults to 0.
% This is negation over a call with an unbound argument (Value) --
% s(CASP) handles this fine, it's not a meta-predicate.
contributes(Actor, Action, Key, 0) :-
    condition_key(Key),
    base_priority(Action, _),
    not has_specific_contribution(Actor, Action, Key).

has_specific_contribution(Actor, Action, Key) :-
    contributes(Actor, Action, Key, _).

% ---- Generic summation over the fixed condition-key list ----
% NOTE: sum_over_keys walks a literal list of keys, built once below --
% not something computed by findall at runtime.
sum_over_keys(_, _, [], 0).
sum_over_keys(Actor, Action, [K|Ks], Total) :-
    contributes(Actor, Action, K, V),
    sum_over_keys(Actor, Action, Ks, Rest),
    Total is V + Rest.

all_condition_keys([enemy_nearby_bonus, low_hp_enemy_bonus,
                     self_low_hp_penalty, self_low_hp_bonus,
                     no_enemy_bonus]).

% ---- ONE generic priority/3 clause, works for every action ----
priority(Actor, Action, P) :-
    base_priority(Action, Base),
    all_condition_keys(Keys),
    sum_over_keys(Actor, Action, Keys, Sum),
    P is Base + Sum.

% ---- Dominance / best-action selection, unchanged ----
dominated(Actor, Action, Priority) :-
    priority(Actor, OtherAction, OtherPriority),
    Action \= OtherAction,
    OtherPriority > Priority.

good_action(Actor, Action) :-
    priority(Actor, Action, Priority),
    not dominated(Actor, Action, Priority).

?- good_action(player, Action).