% Reasoned innocent rules, extended with the half-zombie rules.
% The baseline half_zombie_timed_behavior.pl stays unchanged for comparison.
#include('reasoned_innocent.pl').
#include('half_zombie_rules.pl').

% Match the baseline's immediate-danger definition for half-zombie fallback.
% half_zombie_rules.pl also supplies suspected-human threats through this hook.
immediate_danger_from(Zombie, Actor) :-
    zombie(Zombie),
    distance(Zombie, Distance, Actor),
    Distance #=< 2.
immediate_danger(Actor) :-
    immediate_danger_from(_, Actor).

% Keep these constants equal to half_zombie_timed_behavior.pl.
% FactFactory validates the Unity timer using the baseline file.
bite_window_seconds(5).
critical_bite_seconds(4).

bite_time_remaining(Remaining, Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    Now #>= Start,
    bite_window_seconds(Window),
    Remaining #= Start + Window - Now.

bite_window_active(Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    Now #>= Start,
    bite_window_seconds(Window),
    Now #< Start + Window.

critical_bite_stage(Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    bite_window_seconds(Window),
    critical_bite_seconds(Critical),
    Now #>= Start + Window - Critical,
    Now #< Start + Window.

bite_window_expired(Actor) :-
    half_zombie(Actor),
    current_time(Now),
    bite_window_started_at(Start, Actor),
    bite_window_seconds(Window),
    Now #>= Start + Window.

transformed_zombie(Actor) :-
    bite_window_expired(Actor).

zombie(Actor) :-
    transformed_zombie(Actor).

eliminated(Actor) :-
    transformed_zombie(Actor).

active_half_zombie(Actor) :-
    half_zombie(Actor),
    bite_window_active(Actor),
    not eliminated(Actor).

% During the critical bite window, attacking or approaching a known human
% replaces regular objectives, as in the tested baseline timed policy.
candidate_action(attack_human(Target), Actor) :-
    can_act(Actor),
    active_half_zombie(Actor),
    critical_bite_stage(Actor),
    preferred_human_target(Target, Actor),
    within_bite_range(Target, Actor).

candidate_action(move_to_human(Target), Actor) :-
    can_act(Actor),
    active_half_zombie(Actor),
    critical_bite_stage(Actor),
    preferred_human_target(Target, Actor),
    not within_bite_range(Target, Actor).

% Reasoned innocents already have their own idle candidate.
% The non-innocent fallback is retained from the timed half-zombie rules.
candidate_action(wait, Actor) :-
    can_act(Actor),
    active_half_zombie(Actor),
    not innocent(Actor),
    not critical_bite_stage(Actor),
    not immediate_danger(Actor).

% Expiry eliminates all actions, including the reasoned policy's default wait.
defeated(_, Actor) :-
    eliminated(Actor).

defeated(_, Actor) :-
    half_zombie(Actor),
    not bite_window_active(Actor).

critical_bite_action(attack_human(_)).
critical_bite_action(move_to_human(_)).

defeated(Action, Actor) :-
    critical_bite_stage(Actor),
    not critical_bite_action(Action).
