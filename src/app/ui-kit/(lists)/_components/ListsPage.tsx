"use client";
import React from "react";
import "prismjs/themes/prism.css";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import BasicLists from "@/app/ui-kit/(lists)/_components/BasicLists";
import ListVariants from "@/app/ui-kit/(lists)/_components/ListVariants";
import CustomLists from "@/app/ui-kit/(lists)/_components/CustomLists";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const ListsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Lists"
          title="Ui Kits"
          path={["Lists"]}
          Icon={IconBriefcase}
        />

        <Row className="list-item">
          <PrismCodeWrapper>
            <BasicLists />
            <ListVariants />
            <CustomLists />
          </PrismCodeWrapper>
        </Row>
      </Container>
    </div>
  );
};

export default ListsPage;
