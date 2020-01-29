#!/bin/bash

if [[ ! -z "$(git status --untracked-files=no --porcelain)" ]]; then 
  echo git status is not clean, not doing anything...
  exit 666
fi
sed -i '/\(Dbg\|Trace\|VerifyBoundaryIsCorrect\)(/d' \
       $(find . -path obj -prune -o -name '*.cs')
