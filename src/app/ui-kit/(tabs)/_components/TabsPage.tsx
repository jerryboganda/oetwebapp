"use client";
import React from "react";
import "prismjs/themes/prism.css";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { BasicTabs } from "@/app/ui-kit/(tabs)/_components/BasicTabs";
import OutlineTab from "@/app/ui-kit/(tabs)/_components/OutlineTab";
import LightTab from "@/app/ui-kit/(tabs)/_components/LightTab";
import BackgroundTab from "@/app/ui-kit/(tabs)/_components/BackgroundTab";
import VerticalTab from "@/app/ui-kit/(tabs)/_components/VerticalTab";
import VerticalRightTab from "@/app/ui-kit/(tabs)/_components/VerticalRightTab";
import BottomTab from "@/app/ui-kit/(tabs)/_components/BottomTab";
import JustifyTab from "@/app/ui-kit/(tabs)/_components/JustifyTab";
import ImageTab from "@/app/ui-kit/(tabs)/_components/ImageTab";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const TabsPage = () => {
  return (
    <>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Tabs"
          title="Ui Kits"
          path={["tabs"]}
          Icon={IconBriefcase}
        />
        <Row className="app-tabs-section">
          <PrismCodeWrapper>
            <BasicTabs />
            <OutlineTab />
            <LightTab />
            <BackgroundTab />
            <VerticalTab />
            <VerticalRightTab />
            <BottomTab />
            <JustifyTab />
            <ImageTab />
          </PrismCodeWrapper>
        </Row>
      </Container>
    </>
  );
};

export default TabsPage;
