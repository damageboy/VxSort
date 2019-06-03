``` ini

BenchmarkDotNet=v0.11.5, OS=ubuntu 19.04
Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), 1 CPU, 4 logical and 4 physical cores
.NET Core SDK=3.0.100-preview5-011568
  [Host]     : .NET Core 3.0.0-preview5-27626-15 (CoreCLR 4.6.27622.75, CoreFX 4.700.19.22408), 64bit RyuJIT
  Job-EZTYMZ : .NET Core 3.0.0-preview5-27626-15 (CoreCLR 4.6.27622.75, CoreFX 4.700.19.22408), 64bit RyuJIT

InvocationCount=1  UnrollFactor=1  

```
|          Method |       N |          Mean |         Error |      StdDev |        Median | Ratio | RatioSD |
|---------------- |-------- |--------------:|--------------:|------------:|--------------:|------:|--------:|
|       **ArraySort** |     **100** |      **4.271 us** |     **0.6573 us** |   **1.8432 us** |      **3.828 us** |  **1.00** |    **0.00** |
| QuickSortScalar |     100 |      4.430 us |     0.1675 us |   0.4805 us |      4.282 us |  1.23 |    0.49 |
| QuickSortUnsafe |     100 |      4.262 us |     0.2146 us |   0.5945 us |      4.053 us |  1.17 |    0.45 |
|                 |         |               |               |             |               |       |         |
|       **ArraySort** |    **1000** |     **38.823 us** |     **0.8617 us** |   **2.2851 us** |     **39.291 us** |  **1.00** |    **0.00** |
| QuickSortScalar |    1000 |     55.403 us |     1.6377 us |   1.8203 us |     54.754 us |  1.41 |    0.06 |
| QuickSortUnsafe |    1000 |     49.444 us |     0.9878 us |   2.0177 us |     49.087 us |  1.28 |    0.11 |
|                 |         |               |               |             |               |       |         |
|       **ArraySort** |   **10000** |    **553.825 us** |    **21.2655 us** |  **61.6952 us** |    **545.314 us** |  **1.00** |    **0.00** |
| QuickSortScalar |   10000 |    731.707 us |    23.3253 us |  68.7752 us |    714.879 us |  1.34 |    0.19 |
| QuickSortUnsafe |   10000 |    681.589 us |    20.5813 us |  59.3817 us |    674.067 us |  1.25 |    0.18 |
|                 |         |               |               |             |               |       |         |
|       **ArraySort** |  **100000** |  **6,733.466 us** |   **133.8526 us** | **320.7023 us** |  **6,756.992 us** |  **1.00** |    **0.00** |
| QuickSortScalar |  100000 |  9,031.178 us |   176.6726 us | 314.0354 us |  9,045.999 us |  1.34 |    0.07 |
| QuickSortUnsafe |  100000 |  8,233.265 us |   164.0504 us | 349.6048 us |  8,252.258 us |  1.23 |    0.09 |
|                 |         |               |               |             |               |       |         |
|       **ArraySort** | **1000000** | **72,387.446 us** |   **853.0047 us** | **756.1664 us** | **72,323.517 us** |  **1.00** |    **0.00** |
| QuickSortScalar | 1000000 | 95,863.066 us | 1,121.0982 us | 993.8244 us | 95,617.738 us |  1.32 |    0.02 |
| QuickSortUnsafe | 1000000 | 87,073.738 us |   808.2817 us | 716.5207 us | 86,996.952 us |  1.20 |    0.02 |
