# VxSort [![Build](https://github.com/damageboy/VxSort/workflows/Build/badge.svg?branch=master)](https://github.com/damageboy/vxsort/actions) [![NuGet](https://img.shields.io/nuget/v/VxSort.svg)](https://www.nuget.org/packages/VxSort/)



<img align="right" width="160px" height="160px" src="vxsort.svg">

VxSort is a repository that contains both the code accompanying the [This goes to Eleven](https://bits.houmus.org/2020-01-28/this-goes-to-eleven-pt1) blog post series by [@damageboy](https://github.com/damageboy/). 

In addition, this repository contains the source code for the NuGet package by the same name that provides a ready to use implementation for sorting with managed code at a much higher speeds than what is currently possible with CoreCLR 3.0.

## Usage

Add with Nuget.

```csharp
using VxSort;

// ...
var r = new Random((int) DateTime.UtcNow.Ticks);
int[] lotOfNumbers = Enumerable.Repeat(100_000_000).Select(r.NextInt()).ToArray();

VectorizedSort.Sort(lotsOfNumbers);

// Wow
```

## Roadmap to 1.0

Currently, VxSort is very feature-less, Here's what it **can** do:

- [x] Sort 32-bit integers, ascending

Here's what's **missing**, in terms of functionality, and the order at which it should probably be implemented:

- [ ] Primitive Support:
    - [ ] Add 32-bit descending support: [#2](https://github.com/damageboy/VxSort/issues/2)
    - [ ] Add 32-unsigned ascending support]: [#3](https://github.com/damageboy/VxSort/issues/3) (slightly tricky):
      - There is no direct unsigned support in AVX2, e.g. we have:  
        [`_mm256_cmpgt_epi32(__m256i a, __m256i b)`](https://software.intel.com/sites/landingpage/IntrinsicsGuide/#text=_mm256_cmpgt_epi32) / [`CompareGreaterThan(Vector256<Int32>, Vector256<Int32>`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.avx2.comparegreaterthan?view=netcore-3.0#System_Runtime_Intrinsics_X86_Avx2_CompareGreaterThan_System_Runtime_Intrinsics_Vector256_System_Int32__System_Runtime_Intrinsics_Vector256_System_Int32__)  
        but no unsigned variant for the comparison operation.
      - Instead we could:
        - Perform a fake descending partition operation around the value 0, where all `>= 0` are on the left,
          and all "fake" `< 0` values (e.g what is really unsigned values with the top bit set...) go to the right.
        - Procees to partition with ascending semantics the left portion, while partitioning with descensing semantics the right
        - (Unsigned) Profit!
    - [ ] Add 32-bit unsigned descending support.
    - [ ] Add 64-bit signed/unsigned ascending/descending support.
    - [ ] Support 32/64 bit floating point sorting.
      - Try to generalize the 32/64-bit support with generic wrappers to avoid code duplication
    - [ ] 16 bit support (annoying since there is no 16 bit permute so perf will go down doing 16 -> 32 bit and back)
    - [ ] 8 bit support  (annoying since there is no 8 bit permute so perf will go down doing 16 -> 32 bit and back)
- [ ] Key/Value Sorting:
  - [ ] Add a stable variant, tweaking the current double-pumped loop and switching to `PCSort` for stable sorting.  
    This is substantially slower, but such is life
  - [ ] Add an explicit unstable variant of sorting, for those who don't care/need it
- [ ] `IComparer<T>`/`Comparison<T>` -like based vectorized sorting:
  - In general, all hope is lost if `IComparer<T>`/`Comparison<T>` or anything of that sort is provided.
  - Unless the `IComparer<T>`/`Comparison<T>` is essentially some sort of a trivial/primitive "compare the struct/class by comparing member X, For example:  
    An `IComparer<T>`/`Comparison<T>` that is using the 3rd member of `T` which is at a constant offset of 10-bytes into the `T` strcut/class.
  - Those sorts of trivial `IComparer<T>`/`Comparison<T>` could be actually solved for with a AVX2 gather operation:  
    gather all the keys at a given offset and performs the regular vectorized sorting.
  - This would require a new type of API where the user provides (perhaps?) an Expression<Func<T>> that performs the key selection, that could be "reverse-engineered" to
    understand if the expression tree can be reduced to an AVX2 gather operation and so on...
- General / Good practices?:
  - [ ] Transition to code-generating AVX2 Bitonic sorting to avoid maintaining source files with thousands of source lines that could be instead machine generated.

    
## Credits

VxSort is based on the following ideas/papers:
* [Fast Quicksort Implementation Using AVX Instructions](http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.1009.7773&rep=rep1&type=pdf) by Shay Gueron & Vlad Krasnov for the basic idea of the Vectorized Partition Block.
* [Position-counting sort](https://dirtyhandscoding.github.io/posts/vectorizing-small-fixed-size-sort.html) by @dirtyhandscoding

VxSort uses the following projects:

* The Logo `sort` by Markus from the Noun Project

## Author

Dan Shechter, a.k.a @damageboy

