
can_act(Actor) :-
    health(Health, Actor),
    Health #> 0.

critical_health(Actor) :-
    health(Health, Actor),
    Health #=< 1.

low_health(Actor) :-
    health(Health, Actor),
    Health #=< 2.

dead(Actor) :-
    health(Health, Actor),
    Health #=< 0.

immediate_danger_from(Zombie, Actor) :-
    zombie(Zombie),
    distance(Zombie, Distance, Actor),
    Distance #=< 2.

immediate_danger(Actor) :-
    immediate_danger_from(_, Actor).

levers_remaining :-
    levers_to_win(Required),
    levers_pulled_count(Current),
    Current #< Required.

lever_available(Lever, Actor) :-
    lever_visible(Lever, Actor),
    lever_state(Lever, unpulled).

visible_available_lever(Actor) :-
    lever_available(_, Actor).

at_lever(Lever, Actor) :-
    distance(Lever, Distance, Actor),
    Distance #=< 1.

lever_threatened(Lever) :-
    nearest_zombie_distance_to_lever(Lever, Distance),
    Distance #=< 5.

lever_safe(Lever) :-
    nearest_zombie_distance_to_lever(Lever, Distance),
    Distance #> 5.

closer(Target1, Target2, Actor) :-
    distance(Target1, Distance1, Actor),
    distance(Target2, Distance2, Actor),
    Distance1 #< Distance2.

better_lever(Lever1, Lever2, Actor) :-
    lever_available(Lever1, Actor),
    lever_available(Lever2, Actor),
    Lever1 \= Lever2,
    lever_safe(Lever1),
    lever_threatened(Lever2).

better_lever(Lever1, Lever2, Actor) :-
    lever_available(Lever1, Actor),
    lever_available(Lever2, Actor),
    Lever1 \= Lever2,
    lever_safe(Lever1),
    lever_safe(Lever2),
    closer(Lever1, Lever2, Actor).

better_lever(Lever1, Lever2, Actor) :-
    lever_available(Lever1, Actor),
    lever_available(Lever2, Actor),
    Lever1 \= Lever2,
    lever_threatened(Lever1),
    lever_threatened(Lever2),
    nearest_zombie_distance_to_lever(Lever1, ZombieDistance1),
    nearest_zombie_distance_to_lever(Lever2, ZombieDistance2),
    ZombieDistance1 #> ZombieDistance2.

defeated_lever_target(Lever, Actor) :-
    lever_available(Lever, Actor),
    better_lever(BetterLever, Lever, Actor),
    BetterLever \= Lever.

preferred_lever(Lever, Actor) :-
    lever_available(Lever, Actor),
    not defeated_lever_target(Lever, Actor).

at_barricade(Barricade, Actor) :-
    distance(Barricade, Distance, Actor),
    Distance #=< 1.

zombie_at_barricade(Zombie, Barricade) :-
    zombie(Zombie),
    zombie_distance_to_barricade(Barricade, Distance, Zombie),
    Distance #=< 1.

urgent_barricade(Barricade, Actor) :-
    barricade_visible(Barricade, Actor),
    zombie_at_barricade(_, Barricade).

barricade_damaged(Barricade) :-
    barricade_reinforcement(Barricade, Reinforcement),
    Reinforcement #< 100.

barricade_critical(Barricade) :-
    barricade_reinforcement(Barricade, Reinforcement),
    Reinforcement #=< 25.

better_barricade(Barricade1, Barricade2, Actor) :-
    urgent_barricade(Barricade1, Actor),
    urgent_barricade(Barricade2, Actor),
    Barricade1 \= Barricade2,
    barricade_reinforcement(Barricade1, Reinforcement1),
    barricade_reinforcement(Barricade2, Reinforcement2),
    Reinforcement1 #< Reinforcement2.

better_barricade(Barricade1, Barricade2, Actor) :-
    urgent_barricade(Barricade1, Actor),
    urgent_barricade(Barricade2, Actor),
    Barricade1 \= Barricade2,
    barricade_reinforcement(Barricade1, Reinforcement1),
    barricade_reinforcement(Barricade2, Reinforcement2),
    Reinforcement1 #= Reinforcement2,
    closer(Barricade1, Barricade2, Actor).

defeated_barricade_target(Barricade, Actor) :-
    urgent_barricade(Barricade, Actor),
    better_barricade(BetterBarricade, Barricade, Actor),
    BetterBarricade \= Barricade.

preferred_barricade(Barricade, Actor) :-
    urgent_barricade(Barricade, Actor),
    not defeated_barricade_target(Barricade, Actor).

candidate_action(run_from(Entity), Actor) :-
    can_act(Actor),
    immediate_danger_from(Entity, Actor).

candidate_action(hold_barricade(Barricade), Actor) :-
    can_act(Actor),
    innocent(Actor),
    preferred_barricade(Barricade, Actor),
    at_barricade(Barricade, Actor).

candidate_action(move_to_barricade(Barricade), Actor) :-
    can_act(Actor),
    innocent(Actor),
    preferred_barricade(Barricade, Actor),
    not at_barricade(Barricade, Actor).

candidate_action(pull_lever(Lever), Actor) :-
    can_act(Actor),
    innocent(Actor),
    preferred_lever(Lever, Actor),
    at_lever(Lever, Actor).

candidate_action(move_to_lever(Lever), Actor) :-
    can_act(Actor),
    innocent(Actor),
    preferred_lever(Lever, Actor),
    not at_lever(Lever, Actor).

candidate_action(hunt_lever, Actor) :-
    can_act(Actor),
    innocent(Actor),
    levers_remaining,
    not visible_available_lever(Actor).

candidate_action(wait, Actor) :-
    can_act(Actor),
    innocent(Actor),
    not levers_remaining,
    not urgent_barricade(_, Actor),
    not immediate_danger(Actor).

defeated(hunt_lever, Actor) :-
    urgent_barricade(_, Actor).

defeated(move_to_lever(_), Actor) :-
    urgent_barricade(_, Actor).

defeated(pull_lever(_), Actor) :-
    urgent_barricade(_, Actor).

defeated(move_to_barricade(_), Actor) :-
    immediate_danger(Actor).

defeated(hold_barricade(_), Actor) :-
    immediate_danger(Actor).

defeated(hunt_lever, Actor) :-
    immediate_danger(Actor).

defeated(move_to_lever(_), Actor) :-
    immediate_danger(Actor).

defeated(pull_lever(_), Actor) :-
    immediate_danger(Actor).

% Python queries action(Action, Actor).
action(Action, Actor) :-
    candidate_action(Action, Actor),
    not defeated(Action, Actor).
