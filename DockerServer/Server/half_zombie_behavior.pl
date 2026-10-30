% My thoughts: half-zombie behavior should basically just 
% derive from a mix of human and zombie queries, with only a
% few "necessary" rules and actions (namely, the infected
% statuses)

% Historical untimed draft, retained for comparison and its original tests.
% For the current policy (innocent behavior until 15 seconds remain), load
% half_zombie_timed_behavior.pl INSTEAD of this file. Do not load both.
% This earlier draft hunts whenever an eligible human is visible.
% Reuse human objectives, zombie fleeing, health checks and action/2 unchanged.
#include('innocent_behavior.pl').
#include('half_zombie_rules.pl').

candidate_action(attack_human(Target), Actor) :-
    can_act(Actor),
    preferred_human_target(Target, Actor),
    within_bite_range(Target, Actor).

candidate_action(move_to_human(Target), Actor) :-
    can_act(Actor),
    preferred_human_target(Target, Actor),
    not within_bite_range(Target, Actor).

% A half zombie without the innocent role still has an idle fallback.
% In this untimed draft, infected innocents retain human objectives only when
% no eligible human target is visible. The timed draft uses the timer instead.
candidate_action(wait, Actor) :-
    can_act(Actor),
    half_zombie(Actor),
    not innocent(Actor),
    not visible_human_target(Actor),
    not immediate_danger(Actor).

% Draft priority: flee nearby zombies > attack/approach humans > human objectives.
defeated(attack_human(_), Actor) :-
    immediate_danger(Actor).

defeated(move_to_human(_), Actor) :-
    immediate_danger(Actor).

% Block all existing human objectives, including wait, while hunting a human.
defeated(hold_barricade(_), Actor) :-
    visible_human_target(Actor).

defeated(move_to_barricade(_), Actor) :-
    visible_human_target(Actor).

defeated(pull_lever(_), Actor) :-
    visible_human_target(Actor).

defeated(move_to_lever(_), Actor) :-
    visible_human_target(Actor).

defeated(hunt_lever, Actor) :-
    visible_human_target(Actor).

defeated(wait, Actor) :-
    visible_human_target(Actor).

