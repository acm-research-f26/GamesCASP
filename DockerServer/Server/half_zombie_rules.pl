% Infection and observer knowledge are separate.
% bit_human(Attacker, Victim) means a successful infectious bite, not an attempt.
% Draft policy: an infectious bite immediately creates a half zombie.
infected(Actor) :-
    bit_human(_, Actor).

half_zombie(Actor) :-
    infected(Actor).

% A witness suspects the person doing the biting and the person being bitten.
% Separate clauses express OR; each explanation still requires actual evidence.
% not -half_zombie means "no explicit proof of not being a half zombie".
% It is an exception to suspicion, not evidence of infection by itself.
suspected_infected(Observer, Subject) :-
    witnessed_bite(Observer, Subject, _),
    not -half_zombie(Subject).

suspected_infected(Observer, Subject) :-
    witnessed_bite(Observer, _, Subject),
    not -half_zombie(Subject).

% Do not derive half_zombie from suspicion: witnesses can have incomplete knowledge.
% Only uninfected innocents react to their own observations of a nearby suspect.
% Merely having half_zombie(Subject) in the world does not inform every observer.
immediate_danger_from(Subject, Actor) :-
    innocent(Actor),
    not half_zombie(Actor),
    suspected_infected(Actor, Subject),
    Subject \= Actor,
    human_visible(Subject, Actor),
    distance(Subject, Distance, Actor),
    Distance #=< 2.

% innocent is the existing human role; half_zombie is an additional status.
% A bitten innocent may retain that role to reuse human objectives.
human_target(Target, Actor) :-
    half_zombie(Actor),
    innocent(Target),
    Target \= Actor,
    not half_zombie(Target),
    not zombie(Target),
    health(Health, Target),
    Health #> 0,
    human_visible(Target, Actor),
    distance(Target, Distance, Actor),
    Distance #>= 0.

visible_human_target(Actor) :-
    human_target(_, Actor).

better_human_target(Better, Target, Actor) :-
    human_target(Better, Actor),
    human_target(Target, Actor),
    Better \= Target,
    closer(Better, Target, Actor).

% Keep the alternative existential inside a helper, as the lever rules do.
% not better_human_target(_, Target, Actor) can instead bind the anonymous
% variable to a non-better alternative under constructive negation.
defeated_human_target(Target, Actor) :-
    better_human_target(_, Target, Actor).

preferred_human_target(Target, Actor) :-
    human_target(Target, Actor),
    not defeated_human_target(Target, Actor).

% Initial attack radius matches the existing one-unit interaction convention.
within_bite_range(Target, Actor) :-
    distance(Target, Distance, Actor),
    Distance #=< 1.
