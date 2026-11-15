#pragma once
#include <utility>

// Owner outlives flush and the finishing callback, including partial startup and faults.
// The caller maps all exceptions to its ABI error result; none cross native boundaries.
template<class Owner, class Run, class Flush, class Finish, class Failure>
void wgc_owned_lifetime(Owner& owner, Run&& run, Flush&& flush, Finish&& finish, Failure&& failure) noexcept {
    try { run(owner); } catch (...) { failure(); }
    try { flush(owner); } catch (...) { failure(); }
    try { finish(); } catch (...) { failure(); }
}
