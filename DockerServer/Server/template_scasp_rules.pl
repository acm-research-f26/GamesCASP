can_act(Actor) :-
 health(Actor, Health),
 Health #> 0.

critical_health(Actor) :-
 health(Actor, Health),
 Health #=< 1.

low_health(Actor) :-
 health(Actor, Health),
 Health #=< 2.

dead(Actor) :-
 health(Actor, Health),
 Health #=< 0.

immediate_danger_from(Actor, Zombie) :-
 zombie(Zombie),
 distance(Actor, Zombie, Distance),
 Distance #=< 2.

immediate_danger(Actor) :-
 immediate_danger_from(Actor, _Zombie).

levers_remaining :-
 levers_to_win(Required),
 levers_pulled_count(Current),
 Current #< Required.

lever_available(Actor, Lever) :-
 lever_visible(Actor, Lever),
 lever_state(Lever, unpulled).

visible_available_lever(Actor) :-
 lever_available(Actor, _Lever).

at_lever(Actor, Lever) :-
 distance(Actor, Lever, Distance),
 Distance #=< 1.

lever_threatened(Lever) :-
 nearest_zombie_distance_to_lever(Lever, Distance),
 Distance #=< 5.

lever_safe(Lever) :-
 nearest_zombie_distance_to_lever(Lever, Distance),
 Distance #> 5.

closer(Actor, Target1, Target2) :-
 distance(Actor, Target1, Distance1),
 distance(Actor, Target2, Distance2),
 Distance1 #< Distance2.

better_lever(Actor, Lever1, Lever2) :-
 lever_available(Actor, Lever1),
 lever_available(Actor, Lever2),
 Lever1 \= Lever2,
 lever_safe(Lever1),
 lever_threatened(Lever2).

better_lever(Actor, Lever1, Lever2) :-
 lever_available(Actor, Lever1),
 lever_available(Actor, Lever2),
 Lever1 \= Lever2,
 lever_safe(Lever1),
 lever_safe(Lever2),
 closer(Actor, Lever1, Lever2).

better_lever(Actor, Lever1, Lever2) :-
 lever_available(Actor, Lever1),
 lever_available(Actor, Lever2),
 Lever1 \= Lever2,
 lever_threatened(Lever1),
 lever_threatened(Lever2),
 nearest_zombie_distance_to_lever(Lever1, ZombieDistance1),
 nearest_zombie_distance_to_lever(Lever2, ZombieDistance2),
 ZombieDistance1 #> ZombieDistance2.

defeated_lever_target(Actor, Lever) :-
 lever_available(Actor, Lever),
 better_lever(Actor, BetterLever, Lever),
 BetterLever \= Lever.

preferred_lever(Actor, Lever) :-
 lever_available(Actor, Lever),
 not defeated_lever_target(Actor, Lever).

at_barricade(Actor, Barricade) :-
 distance(Actor, Barricade, Distance),
 Distance #=< 1.

zombie_at_barricade(Zombie, Barricade) :-
 zombie(Zombie),
 distance(Zombie, Barricade, Distance),
 Distance #=< 1.

urgent_barricade(Actor, Barricade) :-
 barricade_visible(Actor, Barricade),
 zombie_at_barricade(_Zombie, Barricade).

barricade_damaged(Barricade) :-
 barricade_reinforcement(Barricade, Reinforcement),
 Reinforcement #< 100.

barricade_critical(Barricade) :-
 barricade_reinforcement(Barricade, Reinforcement),
 Reinforcement #=< 25.

better_barricade(Actor, Barricade1, Barricade2) :-
 urgent_barricade(Actor, Barricade1),
 urgent_barricade(Actor, Barricade2),
 Barricade1 \= Barricade2,
 barricade_reinforcement(Barricade1, Reinforcement1),
 barricade_reinforcement(Barricade2, Reinforcement2),
 Reinforcement1 #< Reinforcement2.

better_barricade(Actor, Barricade1, Barricade2) :-
 urgent_barricade(Actor, Barricade1),
 urgent_barricade(Actor, Barricade2),
 Barricade1 \= Barricade2,
 barricade_reinforcement(Barricade1, Reinforcement1),
 barricade_reinforcement(Barricade2, Reinforcement2),
 Reinforcement1 #= Reinforcement2,
 closer(Actor, Barricade1, Barricade2).

defeated_barricade_target(Actor, Barricade) :-
 urgent_barricade(Actor, Barricade),
 better_barricade(Actor, BetterBarricade, Barricade),
 BetterBarricade \= Barricade.

preferred_barricade(Actor, Barricade) :-
 urgent_barricade(Actor, Barricade),
 not defeated_barricade_target(Actor, Barricade).

candidate_action(Actor, run_from(Entity)) :-
 can_act(Actor),
 immediate_danger_from(Actor, Entity).

candidate_action(Actor, hold_barricade(Barricade)) :-
 can_act(Actor),
 innocent(Actor),
 preferred_barricade(Actor, Barricade),
 at_barricade(Actor, Barricade).

candidate_action(Actor, move_to_barricade(Barricade)) :-
 can_act(Actor),
 innocent(Actor),
 preferred_barricade(Actor, Barricade),
 not at_barricade(Actor, Barricade).

candidate_action(Actor, pull_lever(Lever)) :-
 can_act(Actor),
 innocent(Actor),
 preferred_lever(Actor, Lever),
 at_lever(Actor, Lever).

candidate_action(Actor, move_to_lever(Lever)) :-
 can_act(Actor),
 innocent(Actor),
 preferred_lever(Actor, Lever),
 not at_lever(Actor, Lever).

candidate_action(Actor, hunt_lever) :-
 can_act(Actor),
 innocent(Actor),
 levers_remaining,
 not visible_available_lever(Actor).

candidate_action(Actor, wait) :-
 can_act(Actor),
 innocent(Actor),
 not levers_remaining,
 not urgent_barricade(Actor, _Barricade),
 not immediate_danger(Actor).

defeated(Actor, hunt_lever) :-
 urgent_barricade(Actor, _Barricade).

defeated(Actor, move_to_lever(_Lever)) :-
 urgent_barricade(Actor, _Barricade).

defeated(Actor, pull_lever(_Lever)) :-
 urgent_barricade(Actor, _Barricade).

defeated(Actor, move_to_barricade(_Barricade)) :-
 immediate_danger(Actor).

defeated(Actor, hold_barricade(_Barricade)) :-
 immediate_danger(Actor).

defeated(Actor, hunt_lever) :-
 immediate_danger(Actor).

defeated(Actor, move_to_lever(_Lever)) :-
 immediate_danger(Actor).

defeated(Actor, pull_lever(_Lever)) :-
 immediate_danger(Actor).

action(Actor, Action) :-
 candidate_action(Actor, Action),
 not defeated(Actor, Action).