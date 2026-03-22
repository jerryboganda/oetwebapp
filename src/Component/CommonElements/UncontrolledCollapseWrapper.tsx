"use client";
import React from "react";
import { UncontrolledCollapse } from "reactstrap";

export default function UncontrolledCollapseWrapper({
  children,
  toggler,
  ...props
}: {
  children: React.ReactNode;
  toggler: string;
}) {
  return (
    <UncontrolledCollapse toggler={toggler} {...props}>
      {children}
    </UncontrolledCollapse>
  );
}
