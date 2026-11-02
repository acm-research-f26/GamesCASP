% Entry point for the "reasoned" policy (see reasoned_innocent.pl).
% The live default remains half_zombie_timed_behavior.pl ("baseline"), which
% includes innocent_behavior.pl. When the half-zombie work is merged, include
% half_zombie_rules.pl here as well; its candidate_action/defeated clauses use
% the same interface, and timed half-zombie candidates need ranks via candidate/3.
#include('reasoned_innocent.pl').
