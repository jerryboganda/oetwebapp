"use client";
import React, { useEffect } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "prismjs/themes/prism.css";
import { Row, Container } from "reactstrap";
import SimpleAccordian from "@/app/ui-kit/(accordions)/_components/SimpleAccordian";
import OutlineAccordian from "@/app/ui-kit/(accordions)/_components/OutlineAccordian";
import IconAccordian from "@/app/ui-kit/(accordions)/_components/IconAccordian";
import NestedAccordian from "@/app/ui-kit/(accordions)/_components/NestedAccordian";
import { IconBriefcase } from "@tabler/icons-react";

const AccordionsPage = () => {
  useEffect(() => {
    if (typeof window !== "undefined") {
      import("prismjs").then((Prism) => {
        Prism.highlightAll();
      });
    }
  }, []);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Accordions"
          title="Ui Kits"
          path={["Accordions"]}
          Icon={IconBriefcase}
        />
        <Row className=" accordions-rtl">
          <SimpleAccordian />
          <OutlineAccordian />
          <IconAccordian />
          <NestedAccordian />
        </Row>
      </Container>
    </div>
  );
};

export default AccordionsPage;
