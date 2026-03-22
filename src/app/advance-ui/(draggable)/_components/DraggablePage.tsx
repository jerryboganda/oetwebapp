"use client";
import React from "react";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import DraggableList from "@/app/advance-ui/(draggable)/_components/DraggableList";
import ClonicDraggableList from "@/app/advance-ui/(draggable)/_components/ClonicDraggableList";
import HandleDraggableList from "@/app/advance-ui/(draggable)/_components/HandleDraggableList";
import DraggableGrid from "@/app/advance-ui/(draggable)/_components/DraggableGrid";
import NestedSortableList from "@/app/advance-ui/(draggable)/_components/NestedSortableList";
import DraggableCardList from "@/app/advance-ui/(draggable)/_components/DraggableCardList";
import { IconBriefcase } from "@tabler/icons-react";

const DraggablePage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Draggable"
          title="Advance Ui"
          path={["Draggable"]}
          Icon={IconBriefcase}
        />
        <Row className="share-list-responsive">
          <DraggableList />
          <ClonicDraggableList />
          <HandleDraggableList />
          <DraggableGrid />
          <NestedSortableList />
        </Row>
        <DraggableCardList />
      </Container>
    </div>
  );
};

export default DraggablePage;
