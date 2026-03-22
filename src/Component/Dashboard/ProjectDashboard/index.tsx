"use client";
import React from "react";
import { Container, Row } from "reactstrap";

import ProjectCard from "./ProjectCard";
import Effortless from "./Effortless";
import ProjectTable from "./ProjectTable";
import ProjectTask from "./ProjectTask";
import Tracker from "./Tracker";
import ProjectFileData from "./ProjectFileData";

const ProjectDashboard = () => {
  return (
    <Container fluid className="mt-3">
      <Row>
        <ProjectCard />
        <ProjectFileData />
        <Effortless />
        <Tracker />
        <ProjectTable />
        <ProjectTask />
      </Row>
    </Container>
  );
};

export default ProjectDashboard;
