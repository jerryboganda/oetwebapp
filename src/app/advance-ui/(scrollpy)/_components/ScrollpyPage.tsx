"use client";
import React, { useEffect, useRef } from "react";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import ScrollpyNav from "@/app/advance-ui/(scrollpy)/_components/ScrollpyNav";
import ScrollpyNested from "@/app/advance-ui/(scrollpy)/_components/ScrollpyNested";
import ScrollpyGroup from "@/app/advance-ui/(scrollpy)/_components/ScrollpyGroup";
import ScrollpyAnchors from "@/app/advance-ui/(scrollpy)/_components/ScrollpyAnchors";
import { IconBriefcase } from "@tabler/icons-react";

const ScrollpyPage = () => {
  const scrollSpyRefs = useRef<HTMLElement[]>([]);

  useEffect(() => {
    const initializeScrollSpy = async () => {
      const bootstrap = await import("bootstrap");

      scrollSpyRefs.current.forEach((element) => {
        if (element) {
          bootstrap.ScrollSpy.getOrCreateInstance(element);
        }
      });

      return () => {
        scrollSpyRefs.current.forEach((element) => {
          if (element) {
            const instance = bootstrap.ScrollSpy.getInstance(element);
            instance?.dispose();
          }
        });
      };
    };

    initializeScrollSpy();
  }, []);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Scrollpy"
          title="Advance Ui"
          path={["Scrollpy"]}
          Icon={IconBriefcase}
        />

        <Row>
          <ScrollpyNav />
          <ScrollpyNested />
          <ScrollpyGroup />
          <ScrollpyAnchors />
        </Row>
      </Container>
    </div>
  );
};

export default ScrollpyPage;
