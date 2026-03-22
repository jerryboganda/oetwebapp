"use client";
import React from "react";
import "prismjs/themes/prism.css";
import { Container, Row } from "reactstrap";
import "toastify-js/src/toastify.css";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import DefaultToast from "@/app/ui-kit/(notifications)/_components/DefaultToast";
import NotificationVariants from "@/app/ui-kit/(notifications)/_components/NotificationVariants";
import { IconBriefcase } from "@tabler/icons-react";

const NotificationsPage: React.FC = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Notification"
          title="Ui Kits"
          path={["Notification"]}
          Icon={IconBriefcase}
        />
        <Row>
          <DefaultToast />
          <NotificationVariants />
        </Row>
      </Container>
    </div>
  );
};
export default NotificationsPage;
